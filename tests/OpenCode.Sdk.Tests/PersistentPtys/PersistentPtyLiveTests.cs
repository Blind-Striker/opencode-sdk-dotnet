using System.Globalization;
using System.Text;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The persistent PTY family's live proof against the pinned server (ADR-0021, ADR-0022): one
/// terminal driven end to end where the <c>opencode-pty</c> daemon exists, and the family's
/// daemon-absent answers where it does not. Both arms assert real server answers and neither is a
/// skip - the daemon ships darwin/linux binaries only, so this workstation's Windows leg proves
/// the declared 503 and the daemon-absent arms of every other route, while the hosted Linux and
/// macOS legs prove the round trip. <see cref="PersistentPtyDaemonGate.DaemonExpected"/> selects
/// the arm, and the arm that ran names itself on the console.
/// </summary>
[ClassDataSource<PinnedOpenCodeServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("pinned-opencode-server")]
public sealed class PersistentPtyLiveTests(PinnedOpenCodeServerFixture server)
{
    /// <summary>The framed input protocol this SDK speaks; the server negotiates it at attach.</summary>
    private const int FramedInputProtocol = 1;

    /// <summary>The columns the live resize asks for; the create default is 80.</summary>
    private const int ResizedCols = 100;

    /// <summary>The rows the live resize asks for; the create default is 24.</summary>
    private const int ResizedRows = 30;

    private const string SessionTitle = "persistent pty live";

    private const string TerminalTitle = "sdk persistent pty live";

    /// <summary>
    /// The terminating line feed is Enter here: the daemon's terminals run a Unix line
    /// discipline, so LF submits the line. The normal PTY family's carriage-return finding was
    /// PSReadLine on a Windows console host, which this family never reaches.
    /// </summary>
    private const string EchoCommand = "echo sdk-live\n";

    /// <summary>What the terminal echoes back, then prints again as the command's own output.</summary>
    private const string EchoMarker = "sdk-live";

    /// <summary>The service the daemon-absent 503 names; the one value that tells it from any other 503.</summary>
    private const string DaemonService = "opencode-pty";

    [Test]
    [Timeout(120_000)]
    public async Task Terminal_Lifecycle_Should_Round_Trip_Or_Answer_The_Daemon_Absent_Arm(
        CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });
        var session = await client.Sessions.CreateSessionAsync(
            new SessionCreateRequest { Title = SessionTitle }, cancellationToken: cancellationToken);
        var created = await client.PersistentPtys.CreatePersistentPtyAsync(
            session.Session.Id, CreateRequest(), OpenCodeRequestOptions.NoThrow, cancellationToken);

        if (!PersistentPtyDaemonGate.DaemonExpected)
        {
            await AssertDaemonAbsentArmsAsync(client, session.Session.Id, created, cancellationToken);
            return;
        }

        await AssertRoundTripAsync(client, session.Session.Id, created, cancellationToken);
    }

    /// <summary>
    /// The create body the live arms share: no command, so the server resolves its own default
    /// shell, and no size, so the create defaults (80x24) are what the later resize changes.
    /// </summary>
    private static PersistentPtyCreateRequest CreateRequest() => new()
    {
        Args = [],
        Title = TerminalTitle,
        Env = new Dictionary<string, string>(StringComparer.Ordinal),
    };

    /// <summary>
    /// The arms every route takes where the daemon does not exist. <c>create</c> is the only route
    /// that starts the daemon, so it is the only one that fails; the rest answer their
    /// existence-independent arms, which is what makes this branch assert rather than skip.
    /// </summary>
    private static async Task AssertDaemonAbsentArmsAsync(
        OpenCodeClient client,
        string sessionId,
        PersistentPtyCreateResponse created,
        CancellationToken cancellationToken)
    {
        await Assert.That(created.Status).IsEqualTo(503);
        await Assert.That(created.IsError).IsTrue();
        await Assert.That(created.Error).IsTypeOf<ServiceUnavailableError>();
        var unavailable = created.Error as ServiceUnavailableError;
        await Assert.That(unavailable?.Service).IsEqualTo(DaemonService);

        var listed = await client.PersistentPtys.ListPersistentPtysAsync(
            sessionId, cancellationToken: cancellationToken);
        await Assert.That(listed.PersistentPtys.Count).IsEqualTo(0);

        var read = await client.PersistentPtys.ReadAsync(sessionId, cancellationToken: cancellationToken);
        await Assert.That(read.IsError).IsFalse();
        await Assert.That(read.Read).IsNull();

        var handoff = await client.PersistentPtys.HandoffAsync(cancellationToken: cancellationToken);
        await Assert.That(handoff.Handoff.Handoff).IsNull();

        var shutdown = await client.PersistentPtys.ShutdownAsync(cancellationToken: cancellationToken);
        await Assert.That(shutdown.Status).IsEqualTo(204);

        Console.WriteLine(
            "ppty-live: arm=daemon-absent create=" + Number(created.Status) +
            " service=" + unavailable?.Service +
            " list=" + Number(listed.PersistentPtys.Count) +
            " read=<null> handoff=<null> shutdown=" + Number(shutdown.Status));
    }

    /// <summary>
    /// The full round trip where the daemon exists: the terminal is created, listed, snapshotted,
    /// driven over its live socket, read back over HTTP, and removed.
    /// </summary>
    private static async Task AssertRoundTripAsync(
        OpenCodeClient client,
        string sessionId,
        PersistentPtyCreateResponse created,
        CancellationToken cancellationToken)
    {
        await Assert.That(created.Status).IsEqualTo(200);
        await Assert.That(created.IsError).IsFalse();
        await Assert.That(created.PersistentPty.Status).IsEqualTo(PersistentPtyInfoStatus.Running);
        await Assert.That(created.PersistentPty.SessionId).IsEqualTo(sessionId);

        var ptyId = created.PersistentPty.Id;
        var listed = await client.PersistentPtys.ListPersistentPtysAsync(
            sessionId, cancellationToken: cancellationToken);
        await Assert.That(listed.PersistentPtys.Select(static pty => pty.Id).ToArray()).Contains(ptyId);

        var terminal = client.PersistentPtys.GetPersistentPtyClient(ptyId);
        var snapshot = await terminal.GetSnapshotAsync(cancellationToken: cancellationToken);
        await Assert.That(snapshot.Snapshot.Info.Id).IsEqualTo(ptyId);

        var drive = await DriveTerminalAsync(client, terminal, sessionId, cancellationToken);

        var removed = await terminal.RemovePersistentPtyAsync(cancellationToken: cancellationToken);
        await Assert.That(removed.Status).IsEqualTo(204);

        var remaining = await client.PersistentPtys.ListPersistentPtysAsync(
            sessionId, cancellationToken: cancellationToken);
        await Assert.That(remaining.PersistentPtys.Select(static pty => pty.Id).ToArray()).DoesNotContain(ptyId);

        Console.WriteLine(
            "ppty-live: arm=round-trip id=" + ptyId +
            " status=" + created.PersistentPty.Status +
            " role=" + drive.Role +
            " inputProtocol=" + Number(drive.InputProtocol) +
            " echo=" + EchoMarker +
            " resize=" + Number(drive.Cols) + "x" + Number(drive.Rows) +
            " read=" + EchoMarker +
            " remove=" + Number(removed.Status) +
            " listed-after=absent");
    }

    /// <summary>
    /// Drives the terminal over its live socket: attach, echo, resize, and the HTTP read the
    /// controller attach selected this terminal for. Every wait is bounded by the test's own
    /// token, so an unanswered terminal ends as the test's timeout rather than a private timer.
    /// </summary>
    private static async Task<TerminalDrive> DriveTerminalAsync(
        OpenCodeClient client,
        PersistentPtyClient terminal,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await using var session = await terminal.ConnectAsync(cancellationToken: cancellationToken);

        var attachment = session.Attachment;
        await Assert.That(attachment.Role).IsEqualTo(PersistentPtyRole.Controller);
        await Assert.That(attachment.InputProtocol).IsEqualTo(FramedInputProtocol);

        await session.WriteAsync(Encoding.UTF8.GetBytes(EchoCommand), cancellationToken);
        var echoed = await ReadUntilEchoAsync(session, cancellationToken);
        await Assert.That(echoed).Contains(EchoMarker);

        await session.ResizeAsync(ResizedCols, ResizedRows, cancellationToken);
        var resized = await ReadUntilResizedAsync(session, cancellationToken);

        // The read route answers from the session's most recently controlled terminal, and this
        // connection's controller attach is what selected it - so a payload here is the selection
        // observed, not merely the terminal's existence.
        var read = await client.PersistentPtys.ReadAsync(sessionId, cancellationToken: cancellationToken);
        await Assert.That(read.Read).IsNotNull();
        await Assert.That(read.Read?.Screen.Text).Contains(EchoMarker);

        return new TerminalDrive
        {
            Role = attachment.Role,
            InputProtocol = attachment.InputProtocol,
            Cols = resized.Cols,
            Rows = resized.Rows,
        };
    }

    /// <summary>
    /// Reads output frames until the concatenated bytes carry the echo. The bytes are decoded
    /// together rather than per frame: this family sends raw terminal output, which is free to
    /// split a multi-byte character - or the marker itself - across two frames.
    /// </summary>
    private static async Task<string> ReadUntilEchoAsync(
        PersistentPtySession session, CancellationToken cancellationToken)
    {
        var output = new List<byte>();
        await foreach (var frame in session.ReadAsync(cancellationToken))
        {
            if (frame is not PersistentPtyOutputFrame chunk)
            {
                continue;
            }

            output.AddRange(chunk.Data.ToArray());
            var text = Encoding.UTF8.GetString([.. output]);
            if (text.Contains(EchoMarker, StringComparison.Ordinal))
            {
                return text;
            }
        }

        throw new InvalidOperationException(
            "The persistent terminal closed before it echoed '" + EchoMarker + "'.");
    }

    /// <summary>
    /// Reads until the server reports the resize this session asked for. The enumeration the echo
    /// left behind was disposed by its own loop, so this one starts fresh on the same socket.
    /// </summary>
    private static async Task<PersistentPtyResizedFrame> ReadUntilResizedAsync(
        PersistentPtySession session, CancellationToken cancellationToken)
    {
        await foreach (var frame in session.ReadAsync(cancellationToken))
        {
            if (frame is PersistentPtyResizedFrame resized &&
                resized.Cols == ResizedCols &&
                resized.Rows == ResizedRows)
            {
                return resized;
            }
        }

        throw new InvalidOperationException(
            "The persistent terminal closed before it reported the " +
            Number(ResizedCols) + "x" + Number(ResizedRows) + " resize.");
    }

    /// <summary>Renders one number for the console line and the failure text, culture-free.</summary>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>What the live drive observed; every member is printed evidence.</summary>
    private sealed record TerminalDrive
    {
        /// <summary>The role the server granted this connection.</summary>
        public required PersistentPtyRole Role { get; init; }

        /// <summary>The input protocol the server negotiated.</summary>
        public required int InputProtocol { get; init; }

        /// <summary>The columns the server reported after the resize.</summary>
        public required int Cols { get; init; }

        /// <summary>The rows the server reported after the resize.</summary>
        public required int Rows { get; init; }
    }
}
