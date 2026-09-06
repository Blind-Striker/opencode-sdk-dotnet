namespace OpenCode.Sdk.Tests.Support;

public sealed class OwnedCleanupTests
{
    [Test]
    public async Task CompleteAsync_Should_Retain_Every_Task_Fault_Without_Duplicating_The_Primary()
    {
        var primary = new InvalidOperationException("primary failed");
        var secondary = new InvalidOperationException("secondary failed");
        var cleanup = new OwnedCleanup(TimeSpan.FromSeconds(1));
        cleanup.Own("reader", Task.WhenAll(Task.FromException(primary), Task.FromException(secondary)));

        var thrown = await Assert.That(() => cleanup.CompleteAsync(primary)).Throws<InvalidOperationException>();

        await Assert.That(thrown).IsSameReferenceAs(primary);
        var failures = (AggregateException)primary.Data[OwnedCleanup.FailuresKey]!;
        await Assert.That(failures.InnerExceptions.Single()).IsSameReferenceAs(secondary);
    }

    [Test]
    public async Task CompleteAsync_Should_Append_Nested_Late_Failures_To_The_Attached_Queue()
    {
        var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new OwnedCleanup(TimeSpan.FromMilliseconds(50));
        var outer = new OwnedCleanup(TimeSpan.FromMilliseconds(50));
        inner.Own("inner read", first.Task);
        outer.Own("outer removal", second.Task);
        var primary = new InvalidOperationException("primary failed");
        var firstFailure = new InvalidOperationException("inner late failure");
        var secondFailure = new InvalidOperationException("outer late failure");
        try
        {
            _ = await Assert.That(() => inner.CompleteAsync(primary)).Throws<InvalidOperationException>();
            var attached = primary.Data[OwnedCleanup.LateFailuresKey];
            _ = await Assert.That(() => outer.CompleteAsync(primary)).Throws<InvalidOperationException>();
            await Assert.That(primary.Data[OwnedCleanup.LateFailuresKey]).IsSameReferenceAs(attached);
            first.SetException(firstFailure);
            second.SetException(secondFailure);
            var innerReport = inner.LateFailures;
            var outerReport = outer.LateFailures;
            await Assert.That(innerReport).IsNotNull();
            await Assert.That(outerReport).IsNotNull();
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await innerReport.WaitForAllAsync(observation.Token);
            await outerReport.WaitForAllAsync(observation.Token);

            var failures = (IReadOnlyCollection<KeyValuePair<string, Exception>>)attached!;
            await Assert.That(failures.Select(entry => entry.Value))
                .IsEquivalentTo(new Exception[] { firstFailure, secondFailure });
        }
        finally
        {
            _ = first.TrySetResult(true);
            _ = second.TrySetResult(true);
        }
    }

    [Test]
    public async Task CompleteAsync_Should_Bound_An_Operation_And_Its_Stalled_Cancellation_Independently()
    {
        using var release = new ManualResetEventSlim();
        TaskCompletionSource<bool>? pending = null;
        var primary = new InvalidOperationException("body failed");
        var late = new InvalidOperationException("late operation failed");
        var cleanup = new OwnedCleanup(TimeSpan.FromMilliseconds(50));
        var laterTokenCancelled = true;
        cleanup.Own("stalled operation", async token =>
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = completion;
            using var registration = token.Register(() => _ = release.Wait(TimeSpan.FromSeconds(10)));
            _ = await completion.Task;
        });
        cleanup.Own("later cleanup", token =>
        {
            laterTokenCancelled = token.IsCancellationRequested;
            return Task.CompletedTask;
        });
        try
        {
            var thrown = await Assert.That(() => cleanup.CompleteAsync(primary)).Throws<InvalidOperationException>();

            await Assert.That(thrown).IsSameReferenceAs(primary);
            await Assert.That(laterTokenCancelled).IsFalse();
            var failures = (AggregateException)primary.Data[OwnedCleanup.FailuresKey]!;
            await Assert.That(failures.InnerExceptions.OfType<TimeoutException>()).Count().IsEqualTo(2);
            release.Set();
            await Assert.That(pending).IsNotNull();
            pending.SetException(late);
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var report = cleanup.LateFailures;
            await Assert.That(report).IsNotNull();
            await report.WaitForAllAsync(observation.Token);
            var attached = (IReadOnlyCollection<KeyValuePair<string, Exception>>)primary.Data[OwnedCleanup.LateFailuresKey]!;
            await Assert.That(attached.Single().Value).IsSameReferenceAs(late);
        }
        finally
        {
            release.Set();
            _ = pending?.TrySetResult(true);
        }
    }
}
