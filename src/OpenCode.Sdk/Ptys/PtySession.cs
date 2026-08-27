using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk;

/// <summary>
/// A live pseudo-terminal connection: read the frames the server sends, write the input the
/// terminal receives, and dispose to close. The session owns its socket, so disposing it is the
/// only way to end the connection; the process exit code is never on this wire — a reader that
/// needs it asks <see cref="PtyClient.GetPtyAsync"/>.
/// </summary>
public class PtySession : IAsyncDisposable
{
    /// <summary>
    /// The replay is chunked at 64Ki UTF-16 code units, so one message can reach roughly 192 KiB
    /// of UTF-8. Receiving it in fixed slices keeps the per-session buffer small; the read loop
    /// assembles the fragments.
    /// </summary>
    private const int ReceiveBufferSize = 16 * 1024;

    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly IPtyWebSocket? _socket;
    private int _disposed;
    private int _reading;

    internal PtySession(IPtyWebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PtySession()
    {
    }

    private IPtyWebSocket Socket => _socket ?? throw MockSeam.CreateError("PtySession", "WebSocket");

    /// <summary>
    /// Reads the frames the server sends until it closes the connection normally. One session
    /// carries one active enumeration: message reassembly cannot be shared, so a second
    /// concurrent enumeration is refused.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token ending the read.</param>
    /// <returns>The frames, in the order the server sent them.</returns>
    /// <exception cref="InvalidOperationException">A read enumeration is already active on this session.</exception>
    /// <exception cref="OpenCodeTransportException">The connection failed, closed abnormally, or carried an unreadable control frame.</exception>
    public virtual IAsyncEnumerable<PtyFrame> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadCoreAsync(Socket, cancellationToken);

    /// <summary>
    /// Writes input to the pseudo-terminal as one UTF-8 text message. Sends are serialized: the
    /// socket allows one outstanding send, so concurrent callers queue rather than corrupt the
    /// stream.
    /// </summary>
    /// <param name="input">The input to send; never null.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the message is sent.</returns>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OpenCodeTransportException">The connection failed while sending.</exception>
    public virtual async Task WriteAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var socket = Socket;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) is 1, this);

        var payload = new ArraySegment<byte>(Encoding.UTF8.GetBytes(input));
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-checked behind the gate: a disposal can land while a queued send waits. It sits
            // outside the mapping block deliberately — ObjectDisposedException is in the write
            // phase's fault set, so a refusal raised inside it would be remapped into a transport
            // failure instead of reaching the caller as the misuse it is.
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) is 1, this);

            try
            {
                await socket.SendAsync(payload, cancellationToken).ConfigureAwait(false);
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
    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        GC.SuppressFinalize(this);
        if (_socket is null)
        {
            return;
        }

        try
        {
            _ = await TryCloseAsync(_socket).ConfigureAwait(false);
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

    private async Task<bool> TryCloseAsync(IPtyWebSocket socket)
    {
        // A close-output frame is a send, and the socket allows one outstanding send, so the
        // graceful close takes the same gate every write takes instead of racing an in-flight
        // one. The wait is bounded so a stuck send cannot stall a disposal indefinitely.
        if (!await _sendGate.WaitAsync(GracefulCloseTimeout).ConfigureAwait(false))
        {
            return false;
        }

        using var timeout = new CancellationTokenSource(GracefulCloseTimeout);
        try
        {
            await socket.CloseOutputAsync(timeout.Token).ConfigureAwait(false);
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

    private async IAsyncEnumerable<PtyFrame> ReadCoreAsync(
        IPtyWebSocket socket,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _reading, 1) is 1)
        {
            throw new InvalidOperationException(
                "A 'PtySession' carries one active read enumeration; message reassembly cannot be shared across two.");
        }

        var buffer = new byte[ReceiveBufferSize];
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
                    received = await socket.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (FailureClassification.Handles(exception, FailurePhase.PtyWebSocketRead))
                {
                    if (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _disposed) is 1)
                    {
                        // The caller tore the session down; the socket it disposed going quiet is
                        // the end it asked for, not a fault to report back.
                        break;
                    }

                    throw FailureClassification.Map(exception, FailurePhase.PtyWebSocketRead, cancellationToken);
                }

                if (received.MessageType is WebSocketMessageType.Close)
                {
                    var failure = PtyCloseFailurePolicy.Map(socket.CloseStatus, socket.CloseStatusDescription);
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
                    yield return PtyFrameReader.Read(received.MessageType, buffer, received.Count);
                    continue;
                }

                assembly.Append(buffer, received.Count);
                var assembled = PtyFrameReader.Read(received.MessageType, assembly.Buffer, assembly.Length);
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
