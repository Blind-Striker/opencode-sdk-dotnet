namespace OpenCode.Sdk.Tests.Support;

public sealed class ScriptedTurnEventsTests
{
    [Test]
    public async Task CompleteAsync_Should_Bound_A_Noncooperative_Reader_And_Reach_Later_Cleanup()
    {
        var scenario = new EventDiagnosticScenario { Stall = true };
        var reader = new OwnedEventReader(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(50), CancellationToken.None);
        var events = new ScriptedTurnEvents("reply", "ses_owned", reader);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var removed = false;
        var sessionCleanup = new OwnedSessionCleanup(_ => Task.CompletedTask, token =>
        {
            removed = !token.IsCancellationRequested;
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(1));
        try
        {
            var primary = await Assert.That(async () =>
            {
                _ = await events.CompleteAsync(scenario.EventsAsync(), connected);
            }).Throws<TimeoutException>();
            var observed = (string)primary!.Data[EventDiagnosticSummary.DataKey]!;
            await Assert.That(observed.Length).IsLessThan(2000);
            await Assert.That(observed).Contains("84 events omitted");
            await Assert.That(observed).Contains("event-0000-");
            await Assert.That(observed).Contains("event-0099-");
            await Assert.That(scenario.Pending).IsNotNull();
            await Assert.That(scenario.Pending.Task.IsCompleted).IsFalse();

            var thrown = await Assert.That(() => reader.CompleteAsync(primary)).Throws<TimeoutException>();
            var propagated = await Assert.That(() => sessionCleanup.CompleteAsync(thrown)).Throws<TimeoutException>();
            await Assert.That(propagated).IsSameReferenceAs(primary);
            await Assert.That(removed).IsTrue();
            await Assert.That(primary.Data[EventDiagnosticSummary.DataKey]).IsEqualTo(observed);
            var lateFailure = new InvalidOperationException("late stalled reader failure");
            scenario.Pending.SetException(lateFailure);
            var report = reader.LateFailures;
            await Assert.That(report).IsNotNull();
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await report.WaitForAllAsync(observation.Token);
            var attached = (IReadOnlyCollection<KeyValuePair<string, Exception>>)primary.Data[OwnedCleanup.LateFailuresKey]!;
            await Assert.That(attached.Single().Value).IsSameReferenceAs(lateFailure);
        }
        finally
        {
            _ = scenario.Pending?.TrySetResult(true);
        }
    }

    [Test]
    public async Task ObserveAsync_Should_Bound_First_And_Tail_Diagnostics_When_The_Stream_Ends()
    {
        var scenario = new EventDiagnosticScenario();
        var reader = new OwnedEventReader(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), CancellationToken.None);
        var events = new ScriptedTurnEvents("reply", "ses_owned", reader);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thrown = await Assert.That(async () =>
        {
            _ = await reader.Start(_ => events.ObserveAsync(scenario.EventsAsync(), connected));
        }).Throws<InvalidOperationException>();
        _ = await Assert.That(() => reader.CompleteAsync(thrown)).Throws<InvalidOperationException>();

        await Assert.That(thrown!.Message.Length).IsLessThan(2000);
        await Assert.That(thrown.Message).Contains("event-0000-");
        await Assert.That(thrown.Message).Contains("event-0099-");
        await Assert.That(thrown.Message).Contains("84 events omitted");
        await Assert.That(thrown.Message).DoesNotContain("event-0050-");
    }

    [Test]
    public async Task ObserveAsync_Should_Retain_Observations_On_The_Original_Reader_Failure()
    {
        var failure = new InvalidOperationException("reader transport failed");
        var scenario = new EventDiagnosticScenario { Failure = failure };
        var reader = new OwnedEventReader(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), CancellationToken.None);
        var events = new ScriptedTurnEvents("reply", "ses_owned", reader);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thrown = await Assert.That(async () =>
        {
            _ = await reader.Start(_ => events.ObserveAsync(scenario.EventsAsync(), connected));
        }).Throws<InvalidOperationException>();
        _ = await Assert.That(() => reader.CompleteAsync(thrown)).Throws<InvalidOperationException>();

        await Assert.That(thrown).IsSameReferenceAs(failure);
        var diagnostic = (string)failure.Data[EventDiagnosticSummary.DataKey]!;
        await Assert.That(diagnostic.Length).IsLessThan(2000);
        await Assert.That(diagnostic).Contains("84 events omitted");
    }
}
