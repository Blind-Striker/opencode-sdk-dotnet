using System.Net.WebSockets;
using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// Replays a fixed run of PTY WebSocket messages from memory, one receive at a time bounded by
/// the caller's buffer — exactly the constraint <see cref="PtySession"/> reads under — so a
/// message larger than one receive triggers the same cross-receive reassembly a live socket
/// would produce. The run ends with a normal closure, mirroring how a session's read loop ends
/// without a fault. No socket, no live server.
/// </summary>
internal sealed class CannedPtyWebSocket : IPtyWebSocket
{
    private readonly (WebSocketMessageType Type, byte[] Payload)[] _messages;
    private int _messageIndex;
    private int _offset;
    private bool _closed;

    public CannedPtyWebSocket(IReadOnlyList<(WebSocketMessageType Type, byte[] Payload)> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages = [.. messages];
    }

    public WebSocketCloseStatus? CloseStatus => _closed ? WebSocketCloseStatus.NormalClosure : null;

    public string? CloseStatusDescription => null;

    /// <summary>
    /// Delivers the next fragment of the current message, or a normal-closure close once every
    /// message has been delivered. A message longer than <paramref name="buffer"/> splits across
    /// as many receives as it takes, exactly as <see cref="PtySession"/>'s fixed 16 KiB buffer
    /// would force a live socket to.
    /// </summary>
    public Task<PtyReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (_messageIndex >= _messages.Length)
        {
            _closed = true;
            return Task.FromResult(new PtyReceiveResult(WebSocketMessageType.Close, 0, EndOfMessage: true));
        }

        var (type, payload) = _messages[_messageIndex];
        var count = Math.Min(payload.Length - _offset, buffer.Count);
        Array.Copy(payload, _offset, buffer.Array!, buffer.Offset, count);
        _offset += count;
        var endOfMessage = _offset >= payload.Length;
        if (endOfMessage)
        {
            _messageIndex++;
            _offset = 0;
        }

        return Task.FromResult(new PtyReceiveResult(type, count, endOfMessage));
    }

    /// <summary>Not exercised by the read-path benchmarks; a harmless no-op rather than a refusal.</summary>
    public Task SendAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Not exercised by the read-path benchmarks; a harmless no-op rather than a refusal.</summary>
    public Task CloseOutputAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Rewinds the replay to its first message, so one instance can be read from repeatedly.</summary>
    public void Reset()
    {
        _messageIndex = 0;
        _offset = 0;
        _closed = false;
    }

    public void Dispose()
    {
    }
}
