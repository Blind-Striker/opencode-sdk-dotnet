namespace OpenCode.Sdk.Tests.Support;

public sealed class OwnedOperationTests
{
    [Test]
    public async Task CleanupAsync_Should_Report_Timeout_And_Observe_A_Late_Failure()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateFailure = new InvalidOperationException("late operation failure");
        var cleanup = new OwnedSessionCleanup(
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            TimeSpan.Zero);
        cleanup.MarkTurnCompleted();
        var operation = cleanup.Own(() => completion.Task, () => Task.CompletedTask);

        var thrown = await Assert.That(async () => await cleanup.CompleteAsync(null))
            .Throws<OperationCanceledException>();
        completion.SetException(lateFailure);
        using var observationBudget = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var observed = await operation.WaitForLateObservationAsync(observationBudget.Token);

        await Assert.That(thrown!.CancellationToken.IsCancellationRequested).IsTrue();
        await Assert.That(observed).IsSameReferenceAs(lateFailure);
    }
}
