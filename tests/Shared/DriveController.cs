using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// The repository-owned drive controller (design §7.4): a WebSocket JSON-RPC client for the
/// simulation backend. One JSON-RPC message per WebSocket text message
/// (control-server.ts:149-169); responses correlate by numeric id; llm.request arrives as an
/// id-less notification (simulated-provider.ts:334). Every wait is bounded — an unattached or
/// wedged controller must fail the suite fast, never hang it.
/// </summary>
internal sealed class DriveController : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly ClientWebSocket _socket;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonDocument>> _responses = new();
    private readonly ConcurrentQueue<DriveInvocation> _invocations = new();
    private readonly SemaphoreSlim _invocationSignal = new(0);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _receiveLoop;
    private long _nextId;
    private int _disposed;

    /// <summary>
    /// The receive loop is started here, from the instance constructor, rather than from the
    /// <see cref="ConnectAsync"/> factory below: a Task field assigned by directly calling an
    /// async method from this object's own constructor is a task this object owns, the same
    /// shape <c>LoopbackHttpServer</c>'s accept-loop field uses, so DisposeAsync awaiting it
    /// later does not read as awaiting a foreign task.
    /// </summary>
    private DriveController(ClientWebSocket socket)
    {
        _socket = socket;
        _receiveLoop = ReceiveLoopAsync();
    }

    public static async Task<DriveController> ConnectAsync(Uri backendEndpoint, TimeSpan timeout)
    {
        var socket = new ClientWebSocket();
        using var connectTimeout = new CancellationTokenSource(timeout);
        try
        {
            await socket.ConnectAsync(backendEndpoint, connectTimeout.Token);
            return new DriveController(socket);
        }
        catch (Exception exception)
        {
            socket.Dispose();
            if (exception is OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Could not reach the drive backend at {backendEndpoint} within {timeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s.");
            }

            throw;
        }
    }

    public async Task HandshakeAsync()
    {
        using var response = await RoundTripAsync(DriveProtocol.Handshake, "simulation.handshake");
        var role = response.RootElement.GetProperty("result").GetProperty("role").GetString();
        if (!string.Equals(role, "backend", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected simulation handshake role '{role}'.");
        }
    }

    public async Task AttachAsync()
    {
        using var response = await RoundTripAsync(DriveProtocol.Attach, "llm.attach");
        if (!response.RootElement.GetProperty("result").GetProperty("attached").GetBoolean())
        {
            throw new InvalidOperationException("The drive backend refused the controller attachment.");
        }
    }

    public async Task<DriveInvocation> WaitForRequestAsync(TimeSpan timeout)
    {
        if (!await _invocationSignal.WaitAsync(timeout, _lifetime.Token))
        {
            throw new TimeoutException(
                $"No llm.request arrived within {timeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s.");
        }

        return _invocations.TryDequeue(out var invocation)
            ? invocation
            : throw new InvalidOperationException("The invocation signal fired without a queued invocation.");
    }

    public async Task ChunkTextAsync(string invocationId, params string[] deltas)
    {
        using var response = await RoundTripAsync(
            id => DriveProtocol.ChunkText(id, invocationId, deltas), "llm.chunk");
        EnsureOk(response, "llm.chunk");
    }

    public async Task FinishAsync(string invocationId, string reason = "stop")
    {
        using var response = await RoundTripAsync(
            id => DriveProtocol.Finish(id, invocationId, reason), "llm.finish");
        EnsureOk(response, "llm.finish");
    }

    public async Task DisconnectAsync(string invocationId)
    {
        using var response = await RoundTripAsync(
            id => DriveProtocol.Disconnect(id, invocationId), "llm.disconnect");
        EnsureOk(response, "llm.disconnect");
    }

    public async Task<int> PendingCountAsync()
    {
        using var response = await RoundTripAsync(DriveProtocol.Pending, "llm.pending");
        return response.RootElement.GetProperty("result").GetProperty("invocations").GetArrayLength();
    }

    /// <summary>
    /// Closes the connection: a graceful close first, bounded so an unresponsive peer cannot
    /// stall the caller, then the socket's hard teardown. Disposal is idempotent — a test
    /// fixture teardown path can call this twice (an explicit call beside an
    /// <c>await using</c> scope, or an early failure-path dispose followed by a suite teardown
    /// dispose) — and a second pass must not re-cancel a fresh lifetime, re-close a disposed
    /// socket, or re-dispose the cancellation source, mirroring <c>PtySession</c>'s
    /// Interlocked-guarded DisposeAsync (src/OpenCode.Sdk/Ptys/PtySession.cs).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        await _lifetime.CancelAsync();
        try
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", closeTimeout.Token);
        }
        catch (WebSocketException)
        {
            // The socket may already be gone; the hard dispose below is unconditional.
        }
        catch (OperationCanceledException)
        {
            // The close timeout or an already-cancelled lifetime; the hard dispose still runs.
        }
        catch (ObjectDisposedException)
        {
            // A concurrent teardown path already disposed the socket.
        }
        catch (InvalidOperationException)
        {
            // Close before connect completes reports state, not failure.
        }

        // Read into a local before awaiting: awaiting the field expression directly reads to the
        // analyzer as awaiting a foreign task, the same defensive copy LoopbackHttpServer's
        // DisposeAsync takes for its own accept-loop field.
        var receiveLoop = _receiveLoop;

        // CancellationToken.None deliberately: the lifetime token above is already cancelled, so
        // tying this grace delay to it would resolve WhenAny immediately and give the receive
        // loop no window at all to notice the cancellation and unwind.
        _ = await Task.WhenAny(receiveLoop, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));

        _socket.Dispose();
        _lifetime.Dispose();
    }

    /// <summary>
    /// Reads the model id out of an invocation's provider request body. Upstream sends
    /// <c>{id, url, body}</c> on both the llm.request notification and llm.pending
    /// (simulated-provider.ts:223,264,334), but that body is provider shaped, not protocol
    /// shaped: it is whatever the resolved provider package chose to send. A missing, non-object
    /// body or a missing, non-string <c>model</c> therefore yields null rather than throwing -
    /// the receive loop must still deliver an invocation a caller is parked waiting for, and a
    /// controller that faulted on one optional field would turn a readable assertion failure into
    /// a hang.
    /// </summary>
    private static string? ReadModel(JsonElement parameters) =>
        parameters.TryGetProperty("body", out var body) &&
        body.ValueKind is JsonValueKind.Object &&
        body.TryGetProperty("model", out var model) &&
        model.ValueKind is JsonValueKind.String
            ? model.GetString()
            : null;

    private static void EnsureOk(JsonDocument response, string method)
    {
        if (!response.RootElement.GetProperty("result").GetProperty("ok").GetBoolean())
        {
            throw new InvalidOperationException($"The drive backend did not acknowledge {method}.");
        }
    }

    private async Task<JsonDocument> RoundTripAsync(Func<long, byte[]> compose, string method)
    {
        var id = Interlocked.Increment(ref _nextId);
        var waiter = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        _responses[id] = waiter;
        var payload = compose(id);
        await _sendGate.WaitAsync(_lifetime.Token);
        try
        {
            using var sendTimeout = new CancellationTokenSource(RequestTimeout);
            await _socket.SendAsync(
                new ArraySegment<byte>(payload), WebSocketMessageType.Text, endOfMessage: true, sendTimeout.Token);
        }
        finally
        {
            _ = _sendGate.Release();
        }

        var completed = await Task.WhenAny(waiter.Task, Task.Delay(RequestTimeout, _lifetime.Token));
        if (completed != waiter.Task)
        {
            _ = _responses.TryRemove(id, out _);
            throw new TimeoutException(
                $"The drive backend did not answer {method} (request {id.ToString(CultureInfo.InvariantCulture)}) within {RequestTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s.");
        }

        var response = await waiter.Task;
        if (response.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.GetProperty("message").GetString();
            response.Dispose();
            throw new InvalidOperationException($"Drive request {method} failed: {message}");
        }

        return response;
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult received;
                do
                {
                    received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _lifetime.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

#if NET
                    await message.WriteAsync(buffer.AsMemory(0, received.Count), _lifetime.Token);
#else
                    await message.WriteAsync(buffer, 0, received.Count, _lifetime.Token);
#endif
                }
                while (!received.EndOfMessage);

                Dispatch(JsonDocument.Parse(message.ToArray()));
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal ended the loop.
        }
        catch (WebSocketException)
        {
            // Teardown races are the disposal's business, not the loop's.
        }
        catch (ObjectDisposedException)
        {
            // Disposal already tore the socket down; the loop simply stops.
        }
    }

    private void Dispatch(JsonDocument document)
    {
        var root = document.RootElement;
        if (root.TryGetProperty("id", out var id) &&
            id.ValueKind is JsonValueKind.Number &&
            _responses.TryRemove(id.GetInt64(), out var waiter))
        {
            if (!waiter.TrySetResult(document))
            {
                document.Dispose();
            }

            return;
        }

        using (document)
        {
            if (root.TryGetProperty("method", out var method) &&
                string.Equals(method.GetString(), "llm.request", StringComparison.Ordinal))
            {
                var parameters = root.GetProperty("params");
                _invocations.Enqueue(new DriveInvocation(
                    parameters.GetProperty("id").GetString()!,
                    parameters.GetProperty("url").GetString()!,
                    ReadModel(parameters)));
                _ = _invocationSignal.Release();
            }
        }
    }
}
