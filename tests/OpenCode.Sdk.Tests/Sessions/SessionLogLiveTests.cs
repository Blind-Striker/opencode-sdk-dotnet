using System.Globalization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Sessions;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class SessionLogLiveTests(SimulatedDriveServerFixture server)
{
    private const string ReplayPrompt = "task-three replay prompt";
    private const string ReplayReply = "Task three replay reply.";
    private const string FollowFirstPrompt = "task-three follow first prompt";
    private const string FollowFirstReply = "Task three follow first reply.";
    private const string FollowSecondPrompt = "task-three follow second prompt";
    private const string FollowSecondReply = "Task three follow second reply.";
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);

    [Test]
    [Timeout(180_000)]
    public async Task GetLogAsync_Should_Replay_A_Completed_Turn_And_Resume_Exclusively(
        CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });
        var sessionId = await CreateOwnedSessionAsync(client, workspace.Path, "session-log-replay", cancellationToken);
        var session = client.Sessions.GetSessionClient(sessionId);
        var cleanup = new OwnedSessionCleanup(session, CleanupTimeout);
        Exception? primaryFailure = null;

        try
        {
            var turn = new SimulatedSessionTurn(server, client, session, sessionId);
            var terminal = await turn.CompleteAsync(ReplayPrompt, ReplayReply, cancellationToken);
            cleanup.MarkTurnCompleted();

            var transcript = new SessionLogTranscript(session);
            var replay = await transcript.ReadToEndAsync(
                new SessionLogRequest { Follow = QueryBoolean.False },
                cancellationToken);
            var markers = replay.OfType<EventLogSynced>().ToList();
            await Assert.That(markers).Count().IsEqualTo(1);
            await Assert.That(replay[^1]).IsSameReferenceAs(markers[0]);
            await Assert.That(markers[0].AggregateId).IsEqualTo(sessionId);
            await Assert.That(markers[0].Seq).IsNotNull();
            await Assert.That(replay.Take(replay.Count - 1).All(item => item is ISessionEventDurable)).IsTrue();
            await Assert.That(replay.OfType<SessionCreated>().Any(created => created.Data.SessionId == sessionId)).IsTrue();

            var ownedTexts = replay.OfType<SessionTextEnded>()
                .Where(text => text.Data.SessionId == sessionId && text.Data.Text == ReplayReply)
                .ToList();
            var terminalMatches = replay.OfType<SessionExecutionSucceeded>()
                .Where(succeeded => succeeded.Id == terminal.Id && succeeded.Durable.Seq == terminal.Durable.Seq)
                .ToList();
            await Assert.That(ownedTexts).Count().IsEqualTo(1);
            await Assert.That(terminalMatches).Count().IsEqualTo(1);
            var ownedText = ownedTexts[0];
            var ownedSucceeded = terminalMatches[0];
            var replayTextIndex = FindIndex(replay, ownedText.Id, ownedText.Durable.Seq);
            var replaySucceededIndex = FindIndex(replay, ownedSucceeded.Id, ownedSucceeded.Durable.Seq);
            await Assert.That(replaySucceededIndex).IsGreaterThan(replayTextIndex);
            var anchors = replay.OfType<SessionExecutionStarted>()
                .Where(started => started.Data.SessionId == sessionId && started.Durable.Seq < ownedText.Durable.Seq)
                .OrderByDescending(started => started.Durable.Seq)
                .ToList();
            await Assert.That(anchors).IsNotEmpty();
            var anchor = anchors[0];

            var resumed = await transcript.ReadToEndAsync(
                new SessionLogRequest
                {
                    After = anchor.Durable.Seq.ToString(CultureInfo.InvariantCulture),
                    Follow = QueryBoolean.False,
                },
                cancellationToken);
            var resumedMarkers = resumed.OfType<EventLogSynced>().ToList();
            await Assert.That(resumedMarkers).Count().IsEqualTo(1);
            await Assert.That(resumed[^1]).IsSameReferenceAs(resumedMarkers[0]);
            await Assert.That(resumed.Take(resumed.Count - 1).All(item => item is ISessionEventDurable)).IsTrue();
            await Assert.That(resumed.Any(item => item is SessionExecutionStarted started && started.Id == anchor.Id)).IsFalse();
            var resumedTextIndex = FindIndex(resumed, ownedText.Id, ownedText.Durable.Seq);
            var resumedSucceededIndex = FindIndex(resumed, ownedSucceeded.Id, ownedSucceeded.Durable.Seq);
            await Assert.That(resumedTextIndex).IsGreaterThanOrEqualTo(0);
            await Assert.That(resumedSucceededIndex).IsGreaterThan(resumedTextIndex);
            await Assert.That(resumedMarkers[0].AggregateId).IsEqualTo(sessionId);
            await Assert.That(resumedMarkers[0].Seq).IsNotNull();
            await Assert.That(resumedMarkers[0].Seq!.Value).IsGreaterThanOrEqualTo(ownedSucceeded.Durable.Seq);

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

    [Test]
    [Timeout(180_000)]
    public async Task GetLogAsync_Should_Follow_A_Turn_After_The_Synced_Marker(
        CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });
        var sessionId = await CreateOwnedSessionAsync(client, workspace.Path, "session-log-follow", cancellationToken);
        var session = client.Sessions.GetSessionClient(sessionId);
        var cleanup = new OwnedSessionCleanup(session, CleanupTimeout);
        var transcript = new SessionLogTranscript(session);
        cleanup.Own("session log enumerator", transcript.DisposeAsync);
        Exception? primaryFailure = null;

        try
        {
            var firstTurn = new SimulatedSessionTurn(server, client, session, sessionId);
            _ = await firstTurn.CompleteAsync(FollowFirstPrompt, FollowFirstReply, cancellationToken);
            cleanup.MarkTurnCompleted();
            var finite = await transcript.ReadToEndAsync(
                new SessionLogRequest { Follow = QueryBoolean.False },
                cancellationToken);
            var finiteMarkers = finite.OfType<EventLogSynced>().ToList();
            await Assert.That(finiteMarkers).Count().IsEqualTo(1);
            await Assert.That(finite[^1]).IsSameReferenceAs(finiteMarkers[0]);
            await Assert.That(finiteMarkers[0].Seq).IsNotNull();

            await transcript.AttachAsync(
                finiteMarkers[0].Seq!.Value.ToString(CultureInfo.InvariantCulture),
                cancellationToken);
            await Assert.That(transcript.Marker).IsNotNull();
            await Assert.That(transcript.Marker!.AggregateId).IsEqualTo(sessionId);
            await Assert.That(transcript.ReplayItems.OfType<SessionTextEnded>()
                .Any(text => text.Data.Text == FollowSecondReply)).IsFalse();

            cleanup.MarkTurnStarted();
            var followedTurn = new SimulatedSessionTurn(server, client, session, sessionId);
            var terminal = await followedTurn.CompleteAsync(
                FollowSecondPrompt,
                FollowSecondReply,
                cancellationToken);
            cleanup.MarkTurnCompleted();
            await transcript.ReadTurnAsync(sessionId, FollowSecondReply);

            await Assert.That(transcript.TextEnded).IsNotNull();
            await Assert.That(transcript.Succeeded).IsNotNull();
            await Assert.That(transcript.Succeeded!.Id).IsEqualTo(terminal.Id);
            await Assert.That(transcript.Succeeded.Durable.Seq).IsEqualTo(terminal.Durable.Seq);
            var textIndex = transcript.LiveItems.ToList().IndexOf(transcript.TextEnded!);
            var succeededIndex = transcript.LiveItems.ToList().IndexOf(transcript.Succeeded);
            await Assert.That(textIndex).IsGreaterThanOrEqualTo(0);
            await Assert.That(succeededIndex).IsGreaterThan(textIndex);

            Console.WriteLine(
                "session-log-follow-live: finite-watermark=" + Number(finiteMarkers[0].Seq!.Value) +
                " replay-before-marker=" + Number(transcript.ReplayItems.Count) +
                " followed-text-seq=" + Number(transcript.TextEnded!.Durable.Seq) +
                " followed-succeeded-seq=" + Number(transcript.Succeeded.Durable.Seq));
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

    private static async Task<string> CreateOwnedSessionAsync(
        OpenCodeClient client,
        string directory,
        string title,
        CancellationToken cancellationToken)
    {
        var created = await client.Sessions.CreateSessionAsync(
            new SessionCreateRequest
            {
                Title = title,
                Location = new LocationRef { Directory = directory },
                Model = new ModelRef { Id = "sim-model", ProviderId = "sim" },
            },
            cancellationToken: cancellationToken);
        return created.Session.Id;
    }

    private static int FindIndex(List<ISessionLogItem> items, string id, long sequence) =>
        items.FindIndex(item => item switch
        {
            SessionTextEnded text => text.Id == id && text.Durable.Seq == sequence,
            SessionExecutionSucceeded succeeded => succeeded.Id == id && succeeded.Durable.Seq == sequence,
            _ => false,
        });

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
