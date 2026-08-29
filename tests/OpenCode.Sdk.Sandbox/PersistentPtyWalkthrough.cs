using System.Globalization;
using System.Text;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// The persistent PTY leg of the standing walkthrough (ADR-0021): the daemon-owned terminal
/// family, whose center of gravity is a live WebSocket carrying binary output and framed input -
/// the inverse of the normal PTY family this leg follows. Which arm runs is the server's answer,
/// not a flag: <c>create</c> is the only route that starts the <c>opencode-pty</c> daemon, and the
/// daemon ships darwin/linux binaries only, so on Windows the leg records the declared 503 and the
/// daemon-absent answers of every other route instead of the round trip.
/// </summary>
internal static class PersistentPtyWalkthrough
{
    /// <summary>
    /// Every shell the daemon resolves runs this. The terminating line feed is Enter here: these
    /// terminals run a Unix line discipline, unlike the normal family's Windows console host,
    /// whose PSReadLine needed a carriage return.
    /// </summary>
    private const string EchoCommand = "echo sdk-live\n";

    /// <summary>The text the terminal echoes back, then prints again as the command's own output.</summary>
    private const string EchoMarker = "sdk-live";

    /// <summary>The columns the resize asks for; the create default is 80.</summary>
    private const int ResizedCols = 100;

    /// <summary>The rows the resize asks for; the create default is 24.</summary>
    private const int ResizedRows = 30;

    /// <summary>Bounds every read so an unresponsive terminal ends the leg instead of the process.</summary>
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(20);

    /// <summary>Drives the leg against a live server.</summary>
    /// <param name="client">The registered client family.</param>
    /// <param name="sessionId">The session the terminals belong to; this family is session-keyed.</param>
    /// <returns>A task that completes once the terminal has been removed, or the daemon-absent arms recorded.</returns>
    public static async Task RunAsync(OpenCodeClient client, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var created = await client.PersistentPtys
            .CreatePersistentPtyAsync(sessionId, CreateRequest(), OpenCodeRequestOptions.NoThrow)
            .ConfigureAwait(false);

        if (created.IsError)
        {
            await DescribeDaemonAbsentAsync(client, sessionId, created).ConfigureAwait(false);
            return;
        }

        await RunRoundTripAsync(client, sessionId, created).ConfigureAwait(false);
    }

    /// <summary>
    /// The create body: no command, so the daemon resolves the server's default shell, and no
    /// size, so the create defaults are what the later resize changes.
    /// </summary>
    private static PersistentPtyCreateRequest CreateRequest() => new()
    {
        Args = [],
        Title = "sdk persistent pty demo",
        Env = new Dictionary<string, string>(StringComparer.Ordinal),
    };

    /// <summary>
    /// Records what the family answers where the daemon does not exist. Only <c>create</c> fails:
    /// the others take their existence-independent arms, which is the evidence that the SDK's
    /// nullable payloads and the 204 are real server shapes rather than defensive guesses.
    /// </summary>
    private static async Task DescribeDaemonAbsentAsync(
        OpenCodeClient client, string sessionId, PersistentPtyCreateResponse created)
    {
        var service = (created.Error as ServiceUnavailableError)?.Service ?? "<none>";

        Console.WriteLine($"ppty-create: status={created.Status} service={service} error={ErrorName(created)}");

        var listed = await client.PersistentPtys.ListPersistentPtysAsync(sessionId).ConfigureAwait(false);

        Console.WriteLine($"ppty-list:   status={listed.Status} ptys={listed.PersistentPtys.Count}");

        var read = await client.PersistentPtys.ReadAsync(sessionId).ConfigureAwait(false);

        Console.WriteLine($"ppty-read:   status={read.Status} read={read.Read?.PtyId ?? "<null>"}");

        var handoff = await client.PersistentPtys.HandoffAsync().ConfigureAwait(false);

        Console.WriteLine($"ppty-handoff: status={handoff.Status} handoff={handoff.Handoff.Handoff?.InstanceId ?? "<null>"}");

        var shutdown = await client.PersistentPtys.ShutdownAsync().ConfigureAwait(false);

        Console.WriteLine($"ppty-shutdown: status={shutdown.Status} isError={shutdown.IsError}");
    }

    /// <summary>
    /// The full round trip where the daemon exists: list, attach, echo, resize, the HTTP read the
    /// controller attach selected this terminal for, and the removal.
    /// </summary>
    private static async Task RunRoundTripAsync(
        OpenCodeClient client, string sessionId, PersistentPtyCreateResponse created)
    {
        var terminal = created.PersistentPty;

        Console.WriteLine(
            $"ppty-create: status={created.Status} id={terminal.Id} command={terminal.Command} state={terminal.Status} pid={terminal.Pid} size={terminal.Size.Cols}x{terminal.Size.Rows}");

        var listed = await client.PersistentPtys.ListPersistentPtysAsync(sessionId).ConfigureAwait(false);

        Console.WriteLine($"ppty-list:   status={listed.Status} ptys={listed.PersistentPtys.Count} session={sessionId}");

        var handle = client.PersistentPtys.GetPersistentPtyClient(terminal.Id);

        await DriveAsync(client, handle, sessionId).ConfigureAwait(false);

        var removed = await handle.RemovePersistentPtyAsync(OpenCodeRequestOptions.NoThrow).ConfigureAwait(false);

        Console.WriteLine($"ppty-remove: status={removed.Status} isError={removed.IsError}");

        var remaining = await client.PersistentPtys.ListPersistentPtysAsync(sessionId).ConfigureAwait(false);

        Console.WriteLine($"ppty-gone:   status={remaining.Status} ptys={remaining.PersistentPtys.Count}");
    }

    /// <summary>
    /// Opens the session the designed way - ticket-less, over the client's Basic credential - and
    /// records the attach, the echo round trip, the resize the server answers on the read
    /// enumeration, and the HTTP read that the controller attach made this terminal the answer to.
    /// </summary>
    private static async Task DriveAsync(OpenCodeClient client, PersistentPtyClient handle, string sessionId)
    {
        await using var session = await handle.ConnectAsync().ConfigureAwait(false);

        var attachment = session.Attachment;

        Console.WriteLine(
            $"ppty-attach: role={attachment.Role} inputProtocol={attachment.InputProtocol} attachment={attachment.AttachmentId} size={attachment.Info.Size.Cols}x{attachment.Info.Size.Rows} replayEnd={attachment.Replay.EndOffset} truncated={attachment.Replay.Truncated}");

        await session.WriteAsync(Encoding.UTF8.GetBytes(EchoCommand)).ConfigureAwait(false);

        var echoed = await ReadUntilEchoAsync(session).ConfigureAwait(false);

        Console.WriteLine($"ppty-echo:   chars={echoed.Text.Length} carriesMarker={Carries(echoed.Text)} end={echoed.End}");
        Console.WriteLine($"             text={TerminalExcerpt.Around(echoed.Text, EchoMarker)}");

        await session.ResizeAsync(ResizedCols, ResizedRows).ConfigureAwait(false);

        var resize = await ReadFirstResizeAsync(session).ConfigureAwait(false);

        Console.WriteLine(resize.Frame is { } resized
            ? $"ppty-resize: asked={ResizedCols}x{ResizedRows} got={resized.Cols}x{resized.Rows} generation={resized.Generation} checkpointBytes={resized.Checkpoint.Length} end={resize.End}"
            : $"ppty-resize: asked={ResizedCols}x{ResizedRows} got=<none> within {ReadBudget.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s end={resize.End}");

        var read = await client.PersistentPtys.ReadAsync(sessionId).ConfigureAwait(false);

        Console.WriteLine(read.Read is null
            ? $"ppty-read:   status={read.Status} read=<null>"
            : $"ppty-read:   status={read.Status} pty={read.Read.PtyId} screen={read.Read.Screen.Cols}x{read.Read.Screen.Rows} carriesMarker={Carries(read.Read.Screen.Text)}");
    }

    /// <summary>
    /// Reads output frames until the concatenated bytes carry the echo, or the budget expires.
    /// The bytes are decoded together rather than per frame: this family sends raw terminal
    /// output, which is free to split a multi-byte character - or the marker - across two frames.
    /// </summary>
    private static async Task<TerminalRead> ReadUntilEchoAsync(PersistentPtySession session)
    {
        using var budget = new CancellationTokenSource(ReadBudget);
        var output = new List<byte>();
        var end = TerminalReadEnd.ServerClose;

        try
        {
            await foreach (var frame in session.ReadAsync(budget.Token).ConfigureAwait(false))
            {
                if (frame is not PersistentPtyOutputFrame chunk)
                {
                    continue;
                }

                output.AddRange(chunk.Data.ToArray());
                if (Carries(Encoding.UTF8.GetString([.. output])))
                {
                    end = TerminalReadEnd.Target;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            end = TerminalReadEnd.Budget;
        }

        return new TerminalRead
        {
            Text = Encoding.UTF8.GetString([.. output]),
            End = end,
        };
    }

    /// <summary>
    /// Reads until the server reports its first resize, or the budget expires. The first frame is
    /// the answer whatever it carries: a server that clamped the request to another size is
    /// evidence worth printing, not a frame to read past. The enumeration the echo left behind was
    /// disposed by its own loop, so this one starts fresh on the same socket.
    /// </summary>
    private static async Task<ResizeRead> ReadFirstResizeAsync(PersistentPtySession session)
    {
        using var budget = new CancellationTokenSource(ReadBudget);
        var end = TerminalReadEnd.ServerClose;

        try
        {
            await foreach (var frame in session.ReadAsync(budget.Token).ConfigureAwait(false))
            {
                if (frame is PersistentPtyResizedFrame resized)
                {
                    return new ResizeRead { Frame = resized, End = TerminalReadEnd.Target };
                }
            }
        }
        catch (OperationCanceledException)
        {
            end = TerminalReadEnd.Budget;
        }

        return new ResizeRead { Frame = null, End = end };
    }

    private static bool Carries(string text) => text.Contains(EchoMarker, StringComparison.Ordinal);

    private static string ErrorName(OpenCodeResponse response) => response.Error?.GetType().Name ?? "<untyped>";

    /// <summary>How one bounded read ended; printed beside what it saw.</summary>
    private enum TerminalReadEnd
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

    /// <summary>What one bounded output read observed; every member is printed evidence.</summary>
    private sealed record TerminalRead
    {
        /// <summary>The output bytes this read collected, decoded together.</summary>
        public required string Text { get; init; }

        /// <summary>How the read ended.</summary>
        public required TerminalReadEnd End { get; init; }
    }

    /// <summary>What one bounded wait for the resize observed; every member is printed evidence.</summary>
    private sealed record ResizeRead
    {
        /// <summary>The resize the server reported, or null when none arrived.</summary>
        public required PersistentPtyResizedFrame? Frame { get; init; }

        /// <summary>How the read ended.</summary>
        public required TerminalReadEnd End { get; init; }
    }
}
