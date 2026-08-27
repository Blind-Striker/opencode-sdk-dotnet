using System.Net.WebSockets;
using System.Text;
using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Substitutes the PTY WebSocket seam: replays a scripted receive sequence, records every sent
/// message, refuses overlapping sends, and parks on demand so a test drives a dispose or
/// cancellation race without waiting on wall-clock time.
/// </summary>
internal sealed class ScriptedPtyWebSocket : IPtyWebSocket
{
    private readonly TaskCompletionSource<bool> _disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _parked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Queue<ScriptedPtyReceive> _receives = new();
    private readonly List<byte[]> _sent = [];
    private readonly TaskCompletionSource<bool> _sendEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeSends;
    private TaskCompletionSource<bool>? _sendGate;

    /// <summary>Gets the close status the scripted close step reported.</summary>
    public WebSocketCloseStatus? CloseStatus { get; private set; }

    /// <summary>Gets the close description the scripted close step reported.</summary>
    public string? CloseStatusDescription { get; private set; }

    /// <summary>Gets how many times the session asked for a graceful close.</summary>
    public int CloseOutputCalls { get; private set; }

    /// <summary>Gets how many times the session disposed the socket.</summary>
    public int DisposeCalls { get; private set; }

    /// <summary>Gets the highest number of sends observed inside the socket at once.</summary>
    public int MaxConcurrentSends { get; private set; }

    /// <summary>Gets a task that completes once a scripted park step is reached.</summary>
    public Task Parked => _parked.Task;

    /// <summary>Gets a task that completes once the first send reaches the socket.</summary>
    public Task SendEntered => _sendEntered.Task;

    /// <summary>Gets every message the session sent, in the order the socket saw them.</summary>
    public IReadOnlyList<byte[]> SentMessages => _sent;

    /// <summary>Gets every sent message decoded as UTF-8.</summary>
    public IReadOnlyList<string> SentText => [.. _sent.Select(static message => Encoding.UTF8.GetString(message))];

    /// <summary>Scripts one complete text message.</summary>
    public ScriptedPtyWebSocket Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Bytes(WebSocketMessageType.Text, Encoding.UTF8.GetBytes(text), endOfMessage: true);
    }

    /// <summary>Scripts one text message delivered as several fragments.</summary>
    public ScriptedPtyWebSocket TextFragments(params string[] fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        for (var index = 0; index < fragments.Length; index++)
        {
            _ = Bytes(WebSocketMessageType.Text, Encoding.UTF8.GetBytes(fragments[index]), index == fragments.Length - 1);
        }

        return this;
    }

    /// <summary>Scripts one complete binary message.</summary>
    public ScriptedPtyWebSocket Binary(byte[] payload) => Bytes(WebSocketMessageType.Binary, payload, endOfMessage: true);

    /// <summary>Scripts one binary message delivered as two fragments split at the given offset.</summary>
    public ScriptedPtyWebSocket BinaryFragments(byte[] payload, int splitAt)
    {
        ArgumentNullException.ThrowIfNull(payload);

        _ = Bytes(WebSocketMessageType.Binary, [.. payload.Take(splitAt)], endOfMessage: false);
        return Bytes(WebSocketMessageType.Binary, [.. payload.Skip(splitAt)], endOfMessage: true);
    }

    /// <summary>Scripts a close frame carrying the given status and reason.</summary>
    public ScriptedPtyWebSocket Closing(WebSocketCloseStatus status, string? description = null)
    {
        _receives.Enqueue(new ScriptedPtyReceive
        {
            MessageType = WebSocketMessageType.Close,
            CloseStatus = status,
            CloseStatusDescription = description,
        });
        return this;
    }

    /// <summary>Scripts a receive that throws instead of answering.</summary>
    public ScriptedPtyWebSocket Faulting(Exception fault)
    {
        ArgumentNullException.ThrowIfNull(fault);

        _receives.Enqueue(new ScriptedPtyReceive { Fault = fault });
        return this;
    }

    /// <summary>Scripts a receive that parks until the socket is disposed or the read is canceled.</summary>
    public ScriptedPtyWebSocket Parking()
    {
        _receives.Enqueue(new ScriptedPtyReceive { Parks = true });
        return this;
    }

    /// <summary>Holds every send inside the socket until <see cref="ReleaseSends"/> runs.</summary>
    public ScriptedPtyWebSocket GatingSends()
    {
        _sendGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return this;
    }

    /// <summary>Releases every send the gate is holding.</summary>
    public void ReleaseSends() => _sendGate?.TrySetResult(true);

    /// <summary>Answers the next scripted receive step.</summary>
    public Task<PtyReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (_receives.Count is 0)
        {
            throw new InvalidOperationException("The scripted PTY WebSocket ran out of receive steps.");
        }

        var step = _receives.Dequeue();
        if (step.Parks)
        {
            return ParkAsync(cancellationToken);
        }

        if (step.Fault is not null)
        {
            return Task.FromException<PtyReceiveResult>(step.Fault);
        }

        if (step.MessageType is WebSocketMessageType.Close)
        {
            CloseStatus = step.CloseStatus;
            CloseStatusDescription = step.CloseStatusDescription;
            return Task.FromResult(new PtyReceiveResult(WebSocketMessageType.Close, 0, EndOfMessage: true));
        }

        step.Payload.CopyTo(buffer.Array!, buffer.Offset);
        return Task.FromResult(new PtyReceiveResult(step.MessageType, step.Payload.Length, step.EndOfMessage));
    }

    /// <summary>Records one sent message and refuses a second send entering at the same time.</summary>
    public async Task SendAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _activeSends);
        try
        {
            MaxConcurrentSends = Math.Max(MaxConcurrentSends, active);
            if (active > 1)
            {
                throw new InvalidOperationException("The scripted PTY WebSocket saw two sends at once.");
            }

            var message = new byte[buffer.Count];
            Array.Copy(buffer.Array!, buffer.Offset, message, 0, buffer.Count);
            _sent.Add(message);
            _ = _sendEntered.TrySetResult(true);
            if (_sendGate is not null)
            {
                _ = await _sendGate.Task.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            _ = Interlocked.Decrement(ref _activeSends);
        }
    }

    /// <summary>Records the graceful close the session asks for.</summary>
    public Task CloseOutputAsync(CancellationToken cancellationToken)
    {
        CloseOutputCalls++;
        return Task.CompletedTask;
    }

    /// <summary>Records the hard teardown and releases anything parked on the socket.</summary>
    public void Dispose()
    {
        DisposeCalls++;
        _ = _disposal.TrySetResult(true);
        ReleaseSends();
    }

    private ScriptedPtyWebSocket Bytes(WebSocketMessageType messageType, byte[] payload, bool endOfMessage)
    {
        ArgumentNullException.ThrowIfNull(payload);

        _receives.Enqueue(new ScriptedPtyReceive
        {
            MessageType = messageType,
            Payload = payload,
            EndOfMessage = endOfMessage,
        });
        return this;
    }

    private async Task<PtyReceiveResult> ParkAsync(CancellationToken cancellationToken)
    {
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => canceled.TrySetResult(true));

        _ = _parked.TrySetResult(true);
        _ = await Task.WhenAny(_disposal.Task, canceled.Task);
        cancellationToken.ThrowIfCancellationRequested();
        throw new ObjectDisposedException(nameof(ScriptedPtyWebSocket));
    }
}
