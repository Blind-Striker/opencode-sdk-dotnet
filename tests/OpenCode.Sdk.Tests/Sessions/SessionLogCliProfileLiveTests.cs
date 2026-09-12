using System.Globalization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Sessions;

/// <summary>
/// The reversal trigger for the streaming guide's deployment statement. A server started the way
/// the distributed <c>opencode2</c> CLI starts one runs with event persistence off and offers no
/// switch to turn it on, so a replay answers with the <c>log.synced</c> marker alone however many
/// durable events the session actually committed. Every other session-log proof runs against the
/// simulation host, which enables persistence deliberately, so nothing else in the suite watches
/// the profile an adopter actually installs.
/// </summary>
/// <remarks>
/// <b>When this test fails, upstream started persisting by default.</b> That is a documentation
/// event, not a defect: the marker-only sentence in <c>docs/guide/streaming.md</c>, the known-issue
/// relay in <c>README.md</c>, and the persistence sentence in
/// <c>docs/architecture/client-runtime.md</c>'s server-sent-events section all have to change with
/// it, and the change belongs in <c>CHANGELOG.md</c> because it widens what a default server
/// answers. Observed marker-only on <c>@opencode/cli@0.0.0-beta-19425</c> and earlier, and on the
/// pinned source this fixture runs.
/// The marker's <c>Seq</c> is asserted present, never positive: the aggregate watermark's starting
/// value differs across hosts and nothing upstream pins it.
/// </remarks>
[ClassDataSource<PinnedOpenCodeServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class SessionLogCliProfileLiveTests(PinnedOpenCodeServerFixture server)
{
    private const string RenamedTitle = "session-log-cli-profile renamed";

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);

    [Test]
    [Timeout(180_000)]
    public async Task GetLogAsync_Should_Replay_The_Marker_Alone_On_A_Cli_Profile_Server(
        CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });
        var resolved = (await client.GetLocationAsync(cancellationToken: cancellationToken)).ResolvedLocation;
        var created = await client.Sessions.CreateSessionAsync(
            new SessionCreateRequest
            {
                Title = "session-log-cli-profile",
                Location = new LocationRef
                {
                    Directory = resolved.Directory,
                    WorkspaceId = resolved.WorkspaceId,
                },
            },
            cancellationToken: cancellationToken);
        var sessionId = created.Session.Id;
        var session = client.Sessions.GetSessionClient(sessionId);
        var cleanup = new OwnedSessionCleanup(session, CleanupTimeout);

        // No model turn runs here, so there is never anything to interrupt.
        cleanup.MarkTurnCompleted();
        Exception? primaryFailure = null;

        try
        {
            // Two durable definitions committed: the session creation itself and one rename. A
            // persisting server would replay both before the marker.
            var renamed = await session.RenameSessionAsync(
                new SessionRenameRequest { Title = RenamedTitle }, cancellationToken: cancellationToken);
            await Assert.That(renamed.Status).IsEqualTo(204);
            await Assert.That(renamed.IsError).IsFalse();

            var transcript = new SessionLogTranscript(session);
            var replay = await transcript.ReadToEndAsync(
                new SessionLogRequest { Follow = QueryBoolean.False },
                cancellationToken);

            await Assert.That(replay).Count().IsEqualTo(1);
            await Assert.That(replay[0]).IsTypeOf<EventLogSynced>();
            var marker = (EventLogSynced)replay[0];
            await Assert.That(marker.AggregateId).IsEqualTo(sessionId);
            await Assert.That(marker.Seq).IsNotNull();
            await Assert.That(replay.OfType<ISessionEventDurable>()).IsEmpty();

            Console.WriteLine(
                "session-log-cli-profile: mode=" + (server.IsExternal ? "external" : "owned") +
                " items=" + Number(replay.Count) +
                " durable=" + Number(replay.OfType<ISessionEventDurable>().Count()) +
                " marker=" + marker.Type +
                " seq=" + (marker.Seq?.ToString(CultureInfo.InvariantCulture) ?? "<null>"));
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            await cleanup.CompleteAsync(primaryFailure);
        }
    }

    /// <summary>Renders one number for the console line, culture-free.</summary>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
