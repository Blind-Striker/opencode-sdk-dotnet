using System.Net.WebSockets;
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
    private readonly TerminalSocketCore<PtyFrame>? _core;

    internal PtySession(ITerminalWebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _core = new TerminalSocketCore<PtyFrame>(
            socket,
            PtyFrameDecoder.Instance,
            PtyClosePolicy.Instance,
            typeof(PtySession));
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PtySession()
    {
    }

    private TerminalSocketCore<PtyFrame> Core => _core ?? throw MockSeam.CreateError("PtySession", "WebSocket");

    /// <summary>
    /// Reads the frames the server sends until it closes the connection normally. One session
    /// carries one active enumeration: message reassembly cannot be shared, so a second
    /// concurrent enumeration is refused. Reading after disposal is not an error: unlike
    /// <see cref="WriteAsync"/>, which throws once disposed, the enumeration simply ends empty.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token ending the read.</param>
    /// <returns>The frames, in the order the server sent them.</returns>
    /// <exception cref="InvalidOperationException">A read enumeration is already active on this session.</exception>
    /// <exception cref="OpenCodeTransportException">The connection failed, closed abnormally, or carried an unreadable control frame.</exception>
    public virtual IAsyncEnumerable<PtyFrame> ReadAsync(CancellationToken cancellationToken = default) =>
        Core.ReadAsync(cancellationToken);

    /// <summary>
    /// Writes input to the pseudo-terminal as one UTF-8 text message. Sends are serialized: the
    /// socket allows one outstanding send, so concurrent callers queue rather than corrupt the
    /// stream. A terminal's Enter key is carriage return (<c>\r</c>); to submit a command, end
    /// the line with <c>\r</c> — <c>\n</c> renders the text but never submits it. Unlike
    /// <see cref="ReadAsync"/>, which yields an empty enumeration after disposal, a write after
    /// disposal throws rather than doing nothing.
    /// </summary>
    /// <param name="input">The input to send; never null.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the message is sent.</returns>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OpenCodeTransportException">The connection failed while sending.</exception>
    public virtual Task WriteAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var core = Core;
        var payload = new ArraySegment<byte>(Encoding.UTF8.GetBytes(input));
        return core.SendAsync(payload, WebSocketMessageType.Text, cancellationToken);
    }

    /// <summary>
    /// Closes the connection: a graceful close first, bounded so an unresponsive peer cannot
    /// stall the caller, then the socket's hard teardown. Disposal is idempotent, and a read
    /// waiting on the socket ends as a normal end rather than a fault.
    /// </summary>
    /// <returns>A task that completes once the connection is closed.</returns>
    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_core is not null)
        {
            await _core.DisposeAsync().ConfigureAwait(false);
        }
    }
}
