using System.Globalization;
using System.Text;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// The PTY leg of the standing walkthrough (ADR-0021): the hand-written family's HTTP doors, the
/// token door whose ticket header the SDK applies internally, and the live WebSocket session the
/// family exists for. The session is opened ticket-less over the client's Basic credential — the
/// designed non-browser path — and the leg records the replay boundary, an echo round trip, a
/// cursor resume that must not replay again, and the close a removal produces.
/// </summary>
internal static class PtySessionWalkthrough
{
    /// <summary>
    /// Every shell the server resolves runs this. The terminating carriage return is deliberate:
    /// a terminal's Enter key is CR, and a line feed instead leaves the line unsubmitted — observed
    /// live, where PSReadLine rendered the typed text and then waited forever.
    /// </summary>
    private const string EchoCommand = "echo hello\r";

    /// <summary>The text the terminal echoes back, then prints again as the command's own output.</summary>
    private const string EchoMarker = "hello";

    /// <summary>Bounds every read so an unresponsive terminal ends the leg instead of the process.</summary>
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long the leg keeps draining after the echo. A terminal has no end-of-command marker on
    /// this wire, so the command's own output is collected by letting the stream go quiet.
    /// </summary>
    private static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(3);

    /// <summary>Drives the leg against a live server.</summary>
    /// <param name="client">The registered client family.</param>
    /// <returns>A task that completes once the PTY has been removed.</returns>
    public static async Task RunAsync(OpenCodeClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var created = await client.Ptys.CreatePtyAsync(new PtyCreateRequest
        {
            Title = "sdk pty session demo",
        }).ConfigureAwait(false);

        Console.WriteLine(
            $"pty-create:  status={created.Status} id={created.Pty.Id} command={created.Pty.Command} state={created.Pty.Status} pid={created.Pty.Pid}");

        var listed = await client.Ptys.ListPtysAsync().ConfigureAwait(false);

        Console.WriteLine($"pty-list:    status={listed.Status} ptys={listed.Ptys.Count} directory={listed.Location.Directory}");

        var handle = client.Ptys.GetPtyClient(created.Pty.Id);

        // The handler refuses this request unless it carries 'x-opencode-ticket: 1'. The SDK applies
        // that header internally, so a 200 here is the live proof of the internal header channel.
        var token = await handle.CreateConnectTokenAsync(null, OpenCodeRequestOptions.NoThrow).ConfigureAwait(false);

        Console.WriteLine(token.IsError
            ? $"pty-token:   status={token.Status} error={ErrorName(token)}"
            : $"pty-token:   status={token.Status} ticket=<redacted, {token.ConnectToken.Ticket.Length} chars> expiresIn={token.ConnectToken.ExpiresIn}");

        var replayCursor = await EchoAsync(handle, created.Pty.Id).ConfigureAwait(false);
        var resumeCursor = await ResumeAsync(handle, replayCursor).ConfigureAwait(false);

        await CloseAsync(handle, resumeCursor).ConfigureAwait(false);
    }

    /// <summary>Opens the session the designed way, records the replay boundary, and echoes a command.</summary>
    /// <returns>The absolute output cursor the replay ended at.</returns>
    private static async Task<long> EchoAsync(PtyClient handle, string ptyId)
    {
        await using var session = await handle.ConnectAsync().ConfigureAwait(false);

        // 'ClientWebSocket.ConnectAsync' completes only on '101 Switching Protocols', so reaching
        // this line is the live confirmation that the designed path is accepted: no ticket was
        // minted for this connection, and the Basic credential rode the upgrade request.
        Console.WriteLine(
            $"pty-connect: upgrade answered 101 for {ptyId} — ticket-less, Basic credential on the upgrade request");

        var replay = await ReadAsync(session, PtyStop.Cursor, ReadBudget).ConfigureAwait(false);

        Console.WriteLine($"pty-replay:  {Describe(replay)}");
        Console.WriteLine($"             text={TerminalExcerpt.Excerpt(replay.Text)}");

        await session.WriteAsync(EchoCommand).ConfigureAwait(false);

        Console.WriteLine($"pty-write:   sent {TerminalExcerpt.Excerpt(EchoCommand)}");

        var echo = await ReadAsync(session, PtyStop.Marker, ReadBudget).ConfigureAwait(false);

        Console.WriteLine($"pty-echo:    {Describe(echo)}");
        Console.WriteLine($"             echoed={TerminalExcerpt.Around(echo.Text, EchoMarker)}");

        var settled = await ReadAsync(session, PtyStop.Settle, SettleBudget).ConfigureAwait(false);

        Console.WriteLine($"pty-output:  {Describe(settled)}");
        Console.WriteLine($"             text={TerminalExcerpt.Around(settled.Text, EchoMarker)}");

        return replay.Cursor ?? 0;
    }

    /// <summary>
    /// Reconnects at the cursor the first session reported. The replay must carry only what the
    /// terminal produced after it — the echo — never the buffer the first session already saw.
    /// </summary>
    /// <returns>The absolute output cursor this replay ended at.</returns>
    private static async Task<long> ResumeAsync(PtyClient handle, long cursor)
    {
        await using var session = await handle
            .ConnectAsync(new PtyConnectOptions { Cursor = cursor })
            .ConfigureAwait(false);

        var resume = await ReadAsync(session, PtyStop.Cursor, ReadBudget).ConfigureAwait(false);

        Console.WriteLine($"pty-resume:  from={cursor} {Describe(resume)}");
        Console.WriteLine($"             text={TerminalExcerpt.Excerpt(resume.Text)}");

        return resume.Cursor ?? cursor;
    }

    /// <summary>
    /// Removes the PTY while a read is in flight. The same door at the latest cursor has nothing
    /// left to replay — an empty replay beside a non-empty one over the same PTY is the resume
    /// contract observed rather than asserted — and the removal ends the read as a normal close.
    /// </summary>
    private static async Task CloseAsync(PtyClient handle, long cursor)
    {
        await using var session = await handle
            .ConnectAsync(new PtyConnectOptions { Cursor = cursor })
            .ConfigureAwait(false);

        // Started, not awaited: the read has to be in flight when the removal closes the connection.
        var reading = ReadAsync(session, PtyStop.Close, ReadBudget);

        var removed = await handle.RemovePtyAsync(null, OpenCodeRequestOptions.NoThrow).ConfigureAwait(false);

        Console.WriteLine($"pty-remove:  status={removed.Status} isError={removed.IsError}");

        var closed = await reading.ConfigureAwait(false);

        Console.WriteLine($"pty-close:   from={cursor} {Describe(closed)}");
    }

    private static string ErrorName(OpenCodeResponse response) => response.Error?.GetType().Name ?? "<untyped>";

    private static string Describe(PtyRead read) => string.Create(
        CultureInfo.InvariantCulture,
        $"outputFrames={read.OutputFrames} chars={read.Text.Length} cursorFrames={read.CursorFrames} cursor={read.Cursor?.ToString(CultureInfo.InvariantCulture) ?? "<none>"} end={read.End}");

    /// <summary>Reads one bounded stretch of the session and reports what it saw.</summary>
    private static async Task<PtyRead> ReadAsync(PtySession session, PtyStop stop, TimeSpan window)
    {
        using var budget = new CancellationTokenSource(window);
        var text = new StringBuilder();
        var outputFrames = 0;
        var cursorFrames = 0;
        long? cursor = null;
        var end = PtyEnd.ServerClose;

        try
        {
            await foreach (var frame in session.ReadAsync(budget.Token).ConfigureAwait(false))
            {
                if (frame is PtyCursorFrame control)
                {
                    cursorFrames++;
                    cursor ??= control.Cursor;
                    if (stop is PtyStop.Cursor)
                    {
                        end = PtyEnd.Target;
                        break;
                    }

                    continue;
                }

                if (frame is not PtyOutputFrame output)
                {
                    continue;
                }

                outputFrames++;
                _ = text.Append(output.Text);
                if (stop is PtyStop.Marker && text.ToString().Contains(EchoMarker, StringComparison.Ordinal))
                {
                    end = PtyEnd.Target;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A drain that is meant to run out of time reached what it was waiting for.
            end = stop is PtyStop.Settle ? PtyEnd.Target : PtyEnd.Budget;
        }

        return new PtyRead
        {
            OutputFrames = outputFrames,
            CursorFrames = cursorFrames,
            Cursor = cursor,
            Text = text.ToString(),
            End = end,
        };
    }

    /// <summary>Where one bounded read stops.</summary>
    private enum PtyStop
    {
        /// <summary>At the single control frame that ends the replay.</summary>
        Cursor,

        /// <summary>Once the accumulated output carries the echo.</summary>
        Marker,

        /// <summary>When the stream goes quiet, which is how a command's output ends here.</summary>
        Settle,

        /// <summary>Only when the server closes the connection.</summary>
        Close,
    }

    /// <summary>How one bounded read ended.</summary>
    private enum PtyEnd
    {
        /// <summary>
        /// The enumeration ended on its own, which is the server closing the connection normally;
        /// the SDK surfaces an abnormal close as a fault instead of an end.
        /// </summary>
        ServerClose,

        /// <summary>The read reached what it was waiting for.</summary>
        Target,

        /// <summary>The read budget expired first.</summary>
        Budget,
    }

    /// <summary>What one bounded read observed; every member is printed evidence.</summary>
    private sealed record PtyRead
    {
        public required int OutputFrames { get; init; }

        public required int CursorFrames { get; init; }

        public required long? Cursor { get; init; }

        public required string Text { get; init; }

        public required PtyEnd End { get; init; }
    }
}
