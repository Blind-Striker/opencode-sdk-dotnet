using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// The family-neutral WebSocket lifecycle both terminal sessions share: receive with fragment
/// reassembly, serialized sends, a bounded graceful close, idempotent disposal, and one active
/// read enumeration. What differs between families — how a message decodes, what a close status
/// means — rides the two seams; the owner type names the failures.
/// </summary>
/// <typeparam name="TFrame">The frame type the owning family's read enumeration yields.</typeparam>
internal sealed class TerminalSocketCore<TFrame> : IAsyncDisposable
    where TFrame : class
{
    private readonly ITerminalClosePolicy _closePolicy;
    private readonly ITerminalFrameDecoder<TFrame> _decoder;
    private readonly Type _owner;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ITerminalWebSocket _socket;
    private int _disposed;
    private int _reading;

    public TerminalSocketCore(
        ITerminalWebSocket socket,
        ITerminalFrameDecoder<TFrame> decoder,
        ITerminalClosePolicy closePolicy,
        Type owner)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(closePolicy);
        ArgumentNullException.ThrowIfNull(owner);

        _socket = socket;
        _decoder = decoder;
        _closePolicy = closePolicy;
        _owner = owner;
    }

    /// <summary>Gets whether the core has been disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) is 1;

    /// <summary>
    /// Reads the frames the server sends until it closes the connection normally. One core
    /// carries one active enumeration: message reassembly cannot be shared, so a second
    /// concurrent enumeration is refused. Reading after disposal is not an error: unlike
    /// <see cref="SendAsync"/>, which throws once disposed, the enumeration simply ends empty.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token ending the read.</param>
    /// <returns>The frames, in the order the server sent them.</returns>
    public IAsyncEnumerable<TFrame> ReadAsync(CancellationToken cancellationToken) =>
        ReadCoreAsync(cancellationToken);

    /// <summary>
    /// Sends one complete message of the given type. Sends are serialized: the socket allows one
    /// outstanding send, so concurrent callers queue rather than corrupt the stream.
    /// </summary>
    /// <param name="payload">The bytes to send.</param>
    /// <param name="messageType">The message type to send them as.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the message is sent.</returns>
    public async Task SendAsync(
        ArraySegment<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, _owner);

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-checked behind the gate: a disposal can land while a queued send waits. It sits
            // outside the mapping block deliberately — ObjectDisposedException is in the write
            // phase's fault set, so a refusal raised inside it would be remapped into a transport
            // failure instead of reaching the caller as the misuse it is.
            ObjectDisposedException.ThrowIf(IsDisposed, _owner);

            try
            {
                await _socket.SendAsync(payload, messageType, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (FailureClassification.Handles(exception, FailurePhase.PtyWebSocketWrite))
            {
                throw FailureClassification.Map(exception, FailurePhase.PtyWebSocketWrite, cancellationToken);
            }
        }
        finally
        {
            _ = _sendGate.Release();
        }
    }

    /// <summary>
    /// Closes the connection: a graceful close first, bounded so an unresponsive peer cannot
    /// stall the caller, then the socket's hard teardown. Disposal is idempotent, and a read
    /// waiting on the socket ends as a normal end rather than a fault.
    /// </summary>
    /// <returns>A task that completes once the connection is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        try
        {
            _ = await TryCloseAsync().ConfigureAwait(false);
        }
        finally
        {
            // Unconditional: whatever the graceful close does, a disposal that has already
            // latched _disposed must never leave the socket alive behind it.
            _socket.Dispose();
        }

        // _sendGate is deliberately not disposed. SemaphoreSlim only needs disposal once
        // AvailableWaitHandle has been read, which this class never does, and disposing it here
        // would break the two things disposal must not break: an in-flight write's Release would
        // throw over its own mapped failure, and a queued writer would never be released at all,
        // because Dispose does not complete pending async waiters.
    }

    private async Task<bool> TryCloseAsync()
    {
        // A close-output frame is a send, and the socket allows one outstanding send, so the
        // graceful close takes the same gate every write takes instead of racing an in-flight
        // one. The wait is bounded so a stuck send cannot stall a disposal indefinitely.
        if (!await _sendGate.WaitAsync(TerminalSocketBounds.GracefulCloseTimeout).ConfigureAwait(false))
        {
            return false;
        }

        using var timeout = new CancellationTokenSource(TerminalSocketBounds.GracefulCloseTimeout);
        try
        {
            await _socket.CloseOutputAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (
            FailureClassification.Handles(exception, FailurePhase.PtyWebSocketWrite) ||
            exception is InvalidOperationException)
        {
            // Best effort, and deliberately wider than the write plane's fault set: a socket that
            // refuses a close for a state reason it reports as InvalidOperationException must not
            // escape a disposal. The hard teardown follows unconditionally, and a caller closing a
            // connection has nothing left to do about a close frame that never left.
            return false;
        }
        finally
        {
            _ = _sendGate.Release();
        }
    }

    private async IAsyncEnumerable<TFrame> ReadCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _reading, 1) is 1)
        {
            throw new InvalidOperationException(
                $"A '{_owner.Name}' carries one active read enumeration; message reassembly cannot be shared across two.");
        }

        var buffer = new byte[TerminalSocketBounds.ReceiveBufferSize];
        var segment = new ArraySegment<byte>(buffer);
        PtyMessageAssembler? assembly = null;
        try
        {
            while (true)
            {
                PtyReceiveResult received;

                // A yield cannot sit inside a try that catches, so the receive is guarded alone.
                try
                {
                    received = await _socket.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (FailureClassification.Handles(exception, FailurePhase.PtyWebSocketRead))
                {
                    if (!cancellationToken.IsCancellationRequested && IsDisposed)
                    {
                        // The caller tore the session down; the socket it disposed going quiet is
                        // the end it asked for, not a fault to report back.
                        break;
                    }

                    throw FailureClassification.Map(exception, FailurePhase.PtyWebSocketRead, cancellationToken);
                }

                if (received.MessageType is WebSocketMessageType.Close)
                {
                    var failure = _closePolicy.Map(_socket.CloseStatus, _socket.CloseStatusDescription);
                    if (failure is not null)
                    {
                        throw failure;
                    }

                    break;
                }

                if (!received.EndOfMessage)
                {
                    assembly ??= new PtyMessageAssembler();
                    assembly.Append(buffer, received.Count);
                    continue;
                }

                // An unfragmented message decodes straight from the receive buffer.
                if (assembly is null || assembly.Length is 0)
                {
                    yield return _decoder.Decode(received.MessageType, buffer, received.Count);
                    continue;
                }

                assembly.Append(buffer, received.Count);
                var assembled = _decoder.Decode(received.MessageType, assembly.Buffer, assembly.Length);
                assembly.Reset();
                yield return assembled;
            }
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }
}
