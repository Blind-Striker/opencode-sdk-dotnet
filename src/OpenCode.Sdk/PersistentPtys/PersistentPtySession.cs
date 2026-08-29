using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk;

/// <summary>
/// A live persistent-terminal connection. Opened only after the server's <c>attached</c> frame,
/// so <see cref="Attachment"/> is always known: read the frames the server sends, write framed
/// input carrying the current viewport, resize, and dispose to close. The session owns its
/// socket. Output rides as bytes; a caller feeding an emulator writes them as they are.
/// </summary>
public class PersistentPtySession : IAsyncDisposable
{
    /// <summary>The framed input protocol; a server negotiating anything else is refused at attach.</summary>
    private const int InputProtocolVersion = 1;

    private readonly PersistentPtyAttachment? _attachment;
    private readonly TerminalSocketCore<PersistentPtyFrame>? _core;
    private long _cols;
    private long _rows;

    internal PersistentPtySession(TerminalSocketCore<PersistentPtyFrame> core, PersistentPtyAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(attachment);

        _core = core;
        _attachment = attachment;
        _cols = attachment.Info.Size.Cols;
        _rows = attachment.Info.Size.Rows;
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PersistentPtySession()
    {
    }

    /// <summary>
    /// Gets what the server granted at attach time: identity, negotiated input protocol, terminal
    /// info, role, generation, and replay bounds.
    /// </summary>
    public virtual PersistentPtyAttachment Attachment =>
        _attachment ?? throw MockSeam.CreateError("PersistentPtySession", "Attachment");

    /// <summary>
    /// Reads the frames the server sends until it closes the connection normally. The retained
    /// replay, when any, arrives as output frames bracketed by
    /// <see cref="PersistentPtyReplayCompleteFrame"/>; a resize the server reports updates the
    /// viewport later writes carry, whoever caused it. One session carries one active enumeration:
    /// message reassembly cannot be shared, so a second concurrent enumeration is refused. Reading
    /// after disposal is not an error: unlike <see cref="WriteAsync"/>, which throws once disposed,
    /// the enumeration simply ends empty.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token ending the read.</param>
    /// <returns>The frames, in the order the server sent them.</returns>
    /// <exception cref="InvalidOperationException">A read enumeration is already active on this session.</exception>
    /// <exception cref="OpenCodeTransportException">The connection failed, closed abnormally, or carried an unreadable control frame.</exception>
    public virtual IAsyncEnumerable<PersistentPtyFrame> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadCoreAsync(Core, cancellationToken);

    /// <summary>
    /// Writes terminal input as one framed binary message carrying the current viewport. The
    /// bytes are sent exactly as given, so a caller submitting a command ends the line with the
    /// carriage return its terminal expects. Sends are serialized: the socket allows one
    /// outstanding send, so concurrent callers queue rather than corrupt the stream. Input from a
    /// connection the server attached as an observer is accepted here and dropped there.
    /// </summary>
    /// <param name="input">The input bytes to send.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the message is sent.</returns>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OpenCodeTransportException">The connection failed while sending.</exception>
    public virtual Task WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default) =>
        SendFrameAsync(PersistentPtyInputFrame.InputType, input, cancellationToken);

    /// <summary>
    /// Resizes the terminal through a control frame and records the viewport later writes carry.
    /// The server answers with a <see cref="PersistentPtyResizedFrame"/> on the read enumeration.
    /// </summary>
    /// <param name="cols">The new column count; 1 through 65535.</param>
    /// <param name="rows">The new row count; 1 through 65535.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the control frame is sent.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is zero or beyond the wire's 16-bit field.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OpenCodeTransportException">The connection failed while sending.</exception>
    public virtual Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cols, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cols, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, ushort.MaxValue);

        Volatile.Write(ref _cols, cols);
        Volatile.Write(ref _rows, rows);
        return SendFrameAsync(PersistentPtyInputFrame.ControlType, ReadOnlyMemory<byte>.Empty, cancellationToken);
    }

    /// <summary>
    /// Closes the connection: a graceful close first, bounded so an unresponsive peer cannot
    /// stall the caller, then the socket's hard teardown. Disposal is idempotent, and a read
    /// waiting on the socket ends as a normal end rather than a fault. The terminal itself
    /// outlives the connection; removing it is <see cref="PersistentPtyClient"/>'s door.
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

    /// <summary>
    /// Attaches over an upgraded socket: reads the first frame, which the server sends before any
    /// replay, and refuses anything but an <c>attached</c> frame negotiating input protocol 1. A
    /// terminal that does not exist or a daemon that is not running closes 4404 here rather than
    /// on the first read, because this family upgrades before it checks either.
    /// </summary>
    /// <param name="socket">The upgraded socket; the session takes ownership of it.</param>
    /// <param name="ptyId">The terminal the socket addresses; it names the failures.</param>
    /// <param name="cancellationToken">The cancellation token bounding the first read.</param>
    /// <returns>The attached session.</returns>
    /// <exception cref="OpenCodeTransportException">The server closed before attaching, sent another frame first, or negotiated a protocol this SDK does not speak.</exception>
    internal static async Task<PersistentPtySession> AttachAsync(
        IPtyWebSocket socket,
        string ptyId,
        CancellationToken cancellationToken)
    {
        // Ownership stays local until the very end: an attach that never produced a session must
        // not leave the connection open behind it, and the local is nulled only once the new
        // session has taken the core over (OpenCodeServer.StartAsync's idiom, mirrored here).
        TerminalSocketCore<PersistentPtyFrame>? core = null;
        try
        {
            core = new TerminalSocketCore<PersistentPtyFrame>(
                socket,
                PersistentPtyFrameDecoder.Instance,
                PersistentPtyClosePolicy.Instance,
                typeof(PersistentPtySession));
            var attachment = await ReadAttachmentAsync(core, ptyId, cancellationToken).ConfigureAwait(false);
            var attachedSession = new PersistentPtySession(core, attachment);
            core = null;
            return attachedSession;
        }
        finally
        {
            // The null-conditional release is the shape CA2000 recognizes as the unconditional
            // dispose of a transferable local; AsTask keeps the ValueTask consumed exactly once
            // on the branch that has one, which is CA2012's rule.
            await (core?.DisposeAsync().AsTask() ?? Task.CompletedTask).ConfigureAwait(false);
        }
    }

    private static async Task<PersistentPtyAttachment> ReadAttachmentAsync(
        TerminalSocketCore<PersistentPtyFrame> core,
        string ptyId,
        CancellationToken cancellationToken)
    {
        // Exactly one frame is read, and the enumerator is disposed right after: that releases the
        // core's single-enumeration latch, so the caller's own ReadAsync starts a fresh one.
        var frames = core.ReadAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        PersistentPtyFrame? first;
        try
        {
            first = await frames.MoveNextAsync().ConfigureAwait(false) ? frames.Current : null;
        }
        finally
        {
            await frames.DisposeAsync().ConfigureAwait(false);
        }

        if (first is not PersistentPtyAttachedFrame attached)
        {
            throw new OpenCodeTransportException(first is null
                ? $"The opencode server closed the persistent PTY '{ptyId}' WebSocket before sending the 'attached' frame."
                : $"The opencode server sent a '{first.GetType().Name}' before the persistent PTY '{ptyId}' 'attached' frame.");
        }

        if (attached.Attachment.InputProtocol is not InputProtocolVersion)
        {
            var negotiated = attached.Attachment.InputProtocol.ToString(CultureInfo.InvariantCulture);
            throw new OpenCodeTransportException(
                $"The opencode server negotiated persistent PTY input protocol {negotiated} for '{ptyId}'; this SDK speaks protocol 1 (framed input), so the server is out of date.");
        }

        return attached.Attachment;
    }

    private TerminalSocketCore<PersistentPtyFrame> Core =>
        _core ?? throw MockSeam.CreateError("PersistentPtySession", "WebSocket");

    private async IAsyncEnumerable<PersistentPtyFrame> ReadCoreAsync(
        TerminalSocketCore<PersistentPtyFrame> core,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in core.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (frame is PersistentPtyResizedFrame resized)
            {
                // The viewport is a wire fact, not a caller preference: whoever resized the
                // terminal, the next framed write must carry the size the server now believes.
                Volatile.Write(ref _cols, resized.Cols);
                Volatile.Write(ref _rows, resized.Rows);
            }

            yield return frame;
        }
    }

    private Task SendFrameAsync(byte type, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var core = Core;
        var frame = PersistentPtyInputFrame.Encode(type, Volatile.Read(ref _cols), Volatile.Read(ref _rows), data.Span);
        return core.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, cancellationToken);
    }
}
