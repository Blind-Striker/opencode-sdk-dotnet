using System.Globalization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// Proves the normal PTY family's HTTP lifecycle and live WebSocket replay contract against the
/// accepted server pin. A repository-owned Bun peer acknowledges only executed input, so terminal
/// echo cannot satisfy the assertion.
/// </summary>
[ClassDataSource<PinnedOpenCodeServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class PtySessionLiveTests(PinnedOpenCodeServerFixture server)
{
    private const string Command = "bun";

    private const string InitialTitle = "sdk normal pty live";

    private const string ReadyRecord = "READY";

    private const string UpdatedTitle = "sdk normal pty live updated";

    private readonly FixtureLoader _fixtures = new();
    private readonly PtyLiveScenario _scenario = new(server);

    [Test]
    [Timeout(120_000)]
    public async Task Pty_Should_Create_Execute_Replay_Resume_And_Remove(CancellationToken cancellationToken)
    {
        Exception? primaryFailure = null;
        try
        {
            await _scenario.InitializeAsync(cancellationToken);
            var http = await AssertHttpLifecycleAsync(cancellationToken);
            var socket = await AssertSocketLifecycleAsync(http.Terminal, http.PtyId, cancellationToken);

            Console.WriteLine(
                "pty-live: mode=" + _scenario.Mode +
                " create=" + Number(http.CreateStatus) +
                " id=" + http.PtyId +
                " pid=" + Number(http.Pid) +
                " token-http=" + Number(http.TokenStatus) +
                " socket=basic" +
                " replay-cursor=" + Number(socket.ReplayCursor) +
                " resume-cursor=" + Number(socket.ResumeCursor) +
                " ack-a=" + socket.AcknowledgementA +
                " ack-b=" + socket.AcknowledgementB +
                " remove=" + Number(socket.RemoveStatus) +
                " get-after=" + Number(socket.GetAfterStatus) +
                " remove-after=" + Number(socket.RemoveAfterStatus) +
                " listed-after=" + Number(socket.ListedAfter));
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }

        await _scenario.CleanupAsync(primaryFailure);
    }

    private async Task<PtyHttpEvidence> AssertHttpLifecycleAsync(CancellationToken cancellationToken)
    {
        _scenario.Diagnostics.Enter("create");
        var created = await _scenario.Client.Ptys.CreatePtyAsync(
            new PtyCreateRequest
            {
                Command = Command,
                Args = ["-e", _fixtures.LoadText("Ptys.terminal-peer.js")],
                Cwd = _scenario.Directory,
                Title = InitialTitle,
                Location = _scenario.Location,
            },
            cancellationToken: cancellationToken);

        var ptyId = created.Pty.Id;
        _scenario.Diagnostics.Created(ptyId, created.Pty.Pid, created.Status);
        _scenario.Diagnostics.Enter("HTTP lifecycle");
        var terminal = _scenario.Client.Ptys.GetPtyClient(ptyId);
        _scenario.Own(terminal);

        await Assert.That(created.Status).IsEqualTo(200);
        await Assert.That(created.IsError).IsFalse();
        await Assert.That(created.Pty.Status).IsEqualTo(PtyStatus.Running);
        await Assert.That(created.Pty.Id.Length).IsGreaterThan(0);
        await Assert.That(created.Pty.Pid).IsGreaterThan(0);
        await Assert.That(created.Pty.Command).IsEqualTo(Command);
        await Assert.That(created.Pty.Cwd).IsEqualTo(_scenario.Directory);

        var listed = await _scenario.Client.Ptys.ListPtysAsync(
            new PtyListRequest { Location = _scenario.Location },
            cancellationToken: cancellationToken);
        await Assert.That(listed.Status).IsEqualTo(200);
        await Assert.That(listed.Ptys.Select(static pty => pty.Id).ToArray()).Contains(ptyId);

        var fetched = await terminal.GetPtyAsync(
            new PtyRequest { Location = _scenario.Location },
            cancellationToken: cancellationToken);
        await Assert.That(fetched.Status).IsEqualTo(200);
        await Assert.That(fetched.Pty.Id).IsEqualTo(ptyId);

        var updated = await terminal.PutUpdateAsync(
            new PtyUpdatePutRequest { Title = UpdatedTitle, Location = _scenario.Location },
            cancellationToken: cancellationToken);
        await Assert.That(updated.Status).IsEqualTo(200);
        await Assert.That(updated.Update.Title).IsEqualTo(UpdatedTitle);

        var fetchedAfterUpdate = await terminal.GetPtyAsync(
            new PtyRequest { Location = _scenario.Location },
            cancellationToken: cancellationToken);
        await Assert.That(fetchedAfterUpdate.Pty.Id).IsEqualTo(ptyId);
        await Assert.That(fetchedAfterUpdate.Pty.Title).IsEqualTo(UpdatedTitle);

        var token = await terminal.CreateConnectTokenAsync(
            new PtyConnectTokenPostRequest { Location = _scenario.Location },
            cancellationToken: cancellationToken);
        await Assert.That(token.Status).IsEqualTo(200);
        await Assert.That(token.IsError).IsFalse();
        await Assert.That(token.ConnectToken.Ticket.Length).IsGreaterThan(0);
        await Assert.That(token.ConnectToken.ExpiresIn).IsGreaterThan(0);

        return new PtyHttpEvidence
        {
            Terminal = terminal,
            PtyId = ptyId,
            Pid = created.Pty.Pid,
            CreateStatus = created.Status,
            TokenStatus = token.Status,
        };
    }

    private async Task<PtySocketEvidence> AssertSocketLifecycleAsync(
        PtyClient terminal,
        string ptyId,
        CancellationToken cancellationToken)
    {
        var acknowledgementA = await AssertInitialExecutionAsync(terminal, cancellationToken);
        var replay = await AssertReplayAndResumeAsync(terminal, acknowledgementA, cancellationToken);
        var removal = await AssertRemovalAsync(terminal, ptyId, replay, cancellationToken);

        return new PtySocketEvidence
        {
            AcknowledgementA = acknowledgementA,
            AcknowledgementB = replay.AcknowledgementB,
            ReplayCursor = replay.ReplayCursor,
            ResumeCursor = replay.ResumeCursor,
            RemoveStatus = removal.RemoveStatus,
            GetAfterStatus = removal.GetAfterStatus,
            RemoveAfterStatus = removal.RemoveAfterStatus,
            ListedAfter = removal.ListedAfter,
        };
    }

    private async Task<string> AssertInitialExecutionAsync(
        PtyClient terminal,
        CancellationToken cancellationToken)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var acknowledgement = "ACK:" + nonce;
        _scenario.Diagnostics.Enter("initial connect");
        var session = _scenario.Own(await terminal.ConnectAsync(
            new PtyConnectOptions { Location = _scenario.Location }, cancellationToken));
        var transcript = new PtyLiveTranscript();
        _scenario.Diagnostics.Enter("readiness request", transcript);
        await session.WriteAsync("READY?\r", cancellationToken);
        _scenario.Diagnostics.Enter("initial READY", transcript);
        _ = await transcript.ReadThroughReplayAsync(session, [ReadyRecord], cancellationToken);
        _scenario.Diagnostics.Enter("initial input acknowledgement", transcript);
        await session.WriteAsync("RUN " + nonce + "\r", cancellationToken);
        await transcript.ReadUntilRecordAsync(session, acknowledgement, cancellationToken);
        await AssertReadCancellationReuseAsync(session, cancellationToken);
        await session.DisposeAsync();
        return acknowledgement;
    }

    private async Task AssertReadCancellationReuseAsync(PtySession session, CancellationToken cancellationToken)
    {
        _scenario.Diagnostics.Enter("cancel read");
        var canceled = false;
        while (!canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stopReading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await using var reader = session.ReadAsync(stopReading.Token).GetAsyncEnumerator(stopReading.Token);
            while (true)
            {
                var move = reader.MoveNextAsync();
                var pending = move.AsTask();
                if (pending.IsCompleted)
                {
                    await Assert.That(await pending).IsTrue();
                    continue;
                }

                // Drain any trailing terminal output first. Cancellation is exercised only
                // after a move actually suspends, never on an already-selected frame.
                await stopReading.CancelAsync();
                try
                {
                    await Assert.That(await pending).IsTrue();
                }
                catch (OperationCanceledException) when (stopReading.IsCancellationRequested)
                {
                    canceled = true;
                }

                // Output selected between the incomplete-task observation and cancellation
                // may win. Retry with a fresh token; only cancellation of this pending move
                // supplies the proof, never the next move on an already-canceled token.
                break;
            }
        }

        var nonce = Guid.NewGuid().ToString("N");
        var transcript = new PtyLiveTranscript();
        _scenario.Diagnostics.Enter("execute after read cancellation", transcript);
        await session.WriteAsync("RUN " + nonce + "\r", cancellationToken);
        await transcript.ReadUntilRecordAsync(session, "ACK:" + nonce, cancellationToken);
        Console.WriteLine("pty-live: read-canceled same-connection-execution=" + nonce);
    }

    private async Task<PtyReplayEvidence> AssertReplayAndResumeAsync(
        PtyClient terminal,
        string acknowledgementA,
        CancellationToken cancellationToken)
    {
        _scenario.Diagnostics.Enter("replay connect");
        var replay = _scenario.Own(await terminal.ConnectAsync(
            new PtyConnectOptions { Location = _scenario.Location }, cancellationToken));
        var replayTranscript = new PtyLiveTranscript();
        _scenario.Diagnostics.Enter("replay records", replayTranscript);
        var replayCursor = await replayTranscript.ReadThroughReplayAsync(replay, [], cancellationToken);
        await Assert.That(replayTranscript.ContainsRecord(ReadyRecord)).IsTrue();
        await Assert.That(replayTranscript.ContainsRecord(acknowledgementA)).IsTrue();

        var nonceB = Guid.NewGuid().ToString("N");
        var acknowledgementB = "ACK:" + nonceB;
        _scenario.Diagnostics.Enter("replay input acknowledgement", replayTranscript);
        await replay.WriteAsync("RUN " + nonceB + "\r", cancellationToken);
        await replayTranscript.ReadUntilRecordAsync(replay, acknowledgementB, cancellationToken);
        await replay.DisposeAsync();

        _scenario.Diagnostics.Enter("resume connect");
        var resumed = _scenario.Own(await terminal.ConnectAsync(
            new PtyConnectOptions { Location = _scenario.Location, Cursor = replayCursor }, cancellationToken));
        var resumedTranscript = new PtyLiveTranscript();
        _scenario.Diagnostics.Enter("resume records", resumedTranscript);
        var resumeCursor = await resumedTranscript.ReadThroughReplayAsync(resumed, [], cancellationToken);
        await Assert.That(resumedTranscript.ContainsRecord(acknowledgementB)).IsTrue();
        await Assert.That(resumedTranscript.ContainsRecord(ReadyRecord)).IsFalse();
        await Assert.That(resumedTranscript.ContainsRecord(acknowledgementA)).IsFalse();

        return new PtyReplayEvidence
        {
            Session = resumed,
            Transcript = resumedTranscript,
            AcknowledgementB = acknowledgementB,
            ReplayCursor = replayCursor,
            ResumeCursor = resumeCursor,
        };
    }

    private async Task<PtyRemovalEvidence> AssertRemovalAsync(
        PtyClient terminal,
        string ptyId,
        PtyReplayEvidence replay,
        CancellationToken cancellationToken)
    {
        _scenario.Diagnostics.Enter("removal and socket close", replay.Transcript);
        var completion = replay.Transcript.ReadToCompletionAsync(replay.Session, cancellationToken);
        _scenario.Observe(completion);
        var removed = await terminal.RemovePtyAsync(
            new PtyRemoveRequest { Location = _scenario.Location },
            cancellationToken: cancellationToken);
        if (removed.Status is 204)
        {
            _scenario.MarkRemoved();
        }

        await Assert.That(removed.Status).IsEqualTo(204);
        await completion;

        var missingGet = await terminal.GetPtyAsync(
            new PtyRequest { Location = _scenario.Location },
            OpenCodeRequestOptions.NoThrow,
            cancellationToken);
        await AssertNotFoundAsync(missingGet, ptyId);

        var missingRemove = await terminal.RemovePtyAsync(
            new PtyRemoveRequest { Location = _scenario.Location },
            OpenCodeRequestOptions.NoThrow,
            cancellationToken);
        await AssertNotFoundAsync(missingRemove, ptyId);

        var afterRemoval = await _scenario.Client.Ptys.ListPtysAsync(
            new PtyListRequest { Location = _scenario.Location },
            cancellationToken: cancellationToken);
        await Assert.That(afterRemoval.Ptys.Select(static pty => pty.Id).ToArray()).DoesNotContain(ptyId);

        return new PtyRemovalEvidence
        {
            RemoveStatus = removed.Status,
            GetAfterStatus = missingGet.Status,
            RemoveAfterStatus = missingRemove.Status,
            ListedAfter = afterRemoval.Ptys.Count,
        };
    }

    private static async Task AssertNotFoundAsync(OpenCodeResponse response, string ptyId)
    {
        await Assert.That(response.Status).IsEqualTo(404);
        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Error).IsTypeOf<PtyNotFoundError>();
        var error = response.Error as PtyNotFoundError;
        await Assert.That(error?.PtyId).IsEqualTo(ptyId);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record PtyHttpEvidence
    {
        public required PtyClient Terminal { get; init; }

        public required string PtyId { get; init; }

        public required long Pid { get; init; }

        public required int CreateStatus { get; init; }

        public required int TokenStatus { get; init; }
    }

    private sealed record PtyReplayEvidence
    {
        public required PtySession Session { get; init; }

        public required PtyLiveTranscript Transcript { get; init; }

        public required string AcknowledgementB { get; init; }

        public required long ReplayCursor { get; init; }

        public required long ResumeCursor { get; init; }
    }

    private sealed record PtyRemovalEvidence
    {
        public required int RemoveStatus { get; init; }

        public required int GetAfterStatus { get; init; }

        public required int RemoveAfterStatus { get; init; }

        public required int ListedAfter { get; init; }
    }

    private sealed record PtySocketEvidence
    {
        public required string AcknowledgementA { get; init; }

        public required string AcknowledgementB { get; init; }

        public required long ReplayCursor { get; init; }

        public required long ResumeCursor { get; init; }

        public required int RemoveStatus { get; init; }

        public required int GetAfterStatus { get; init; }

        public required int RemoveAfterStatus { get; init; }

        public required int ListedAfter { get; init; }
    }
}
