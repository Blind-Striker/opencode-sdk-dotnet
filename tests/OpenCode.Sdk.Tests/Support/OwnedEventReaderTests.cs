namespace OpenCode.Sdk.Tests.Support;

public sealed class OwnedEventReaderTests
{
    [Test]
    public async Task CompleteAsync_Should_Preserve_Primary_Reader_And_Later_Session_Failures()
    {
        var owner = new OwnedEventReader(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), CancellationToken.None);
        var primary = new InvalidOperationException("prompt failed");
        var readerFailure = new InvalidOperationException("reader failed");
        var removalFailure = new InvalidOperationException("removal failed");
        _ = owner.Start(_ => Task.FromException(readerFailure));

        var thrown = await Assert.That(() => owner.CompleteAsync(primary)).Throws<InvalidOperationException>();
        var outer = new OwnedSessionCleanup(_ => Task.CompletedTask,
            _ => Task.FromException(removalFailure), TimeSpan.FromSeconds(1));
        _ = await Assert.That(() => outer.CompleteAsync(thrown)).Throws<InvalidOperationException>();

        await Assert.That(thrown).IsSameReferenceAs(primary);
        var failures = (AggregateException)primary.Data[OwnedCleanup.FailuresKey]!;
        await Assert.That(failures.InnerExceptions).IsEquivalentTo(new Exception[] { readerFailure, removalFailure });
    }

    [Test]
    public async Task CompleteAsync_Should_Observe_Reader_After_Cancellation_Callback_Fails()
    {
        var owner = new OwnedEventReader(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), CancellationToken.None);
        var callbackFailure = new InvalidOperationException("cancellation callback failed");
        var readerFailure = new InvalidOperationException("reader also failed");
        var primary = new InvalidOperationException("prompt failed");
        using var registration = owner.Token.Register(() => throw callbackFailure);
        _ = owner.Start(_ => Task.FromException(readerFailure));

        var thrown = await Assert.That(() => owner.CompleteAsync(primary)).Throws<InvalidOperationException>();

        await Assert.That(thrown).IsSameReferenceAs(primary);
        var failures = ((AggregateException)primary.Data[OwnedCleanup.FailuresKey]!).Flatten().InnerExceptions;
        await Assert.That(failures).Contains(callbackFailure);
        await Assert.That(failures).Contains(readerFailure);
    }

    [Test]
    public async Task CompleteAsync_Should_Bound_Stalls_Reach_Removal_And_Attach_Late_Failures()
    {
        using var release = new ManualResetEventSlim();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new OwnedEventReader(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(50), CancellationToken.None);
        var callbackFailure = new InvalidOperationException("late callback failed");
        var readerFailure = new InvalidOperationException("late reader failed");
        var primary = new InvalidOperationException("prompt failed");
        using var registration = owner.Token.Register(() =>
        {
            _ = release.Wait(TimeSpan.FromSeconds(10));
            throw callbackFailure;
        });
        owner.Own(pending.Task);
        var removed = false;
        var outer = new OwnedSessionCleanup(_ => Task.CompletedTask, token =>
        {
            removed = !token.IsCancellationRequested;
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(1));
        try
        {
            var thrown = await Assert.That(() => owner.CompleteAsync(primary)).Throws<InvalidOperationException>();
            _ = await Assert.That(() => outer.CompleteAsync(thrown)).Throws<InvalidOperationException>();
            await Assert.That(removed).IsTrue();
            await Assert.That(thrown).IsSameReferenceAs(primary);
            var failures = (AggregateException)primary.Data[OwnedCleanup.FailuresKey]!;
            await Assert.That(failures.InnerExceptions.OfType<TimeoutException>()).Count().IsEqualTo(2);
            var attached = (IReadOnlyCollection<KeyValuePair<string, Exception>>)primary.Data[OwnedCleanup.LateFailuresKey]!;
            pending.SetException(readerFailure);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var report = owner.LateFailures;
            await Assert.That(report).IsNotNull();
            var observed = await report.WaitForFailureAsync(deadline.Token);
            await Assert.That(observed.Value).IsSameReferenceAs(readerFailure);
            release.Set();
            await report.WaitForAllAsync(deadline.Token);
            await Assert.That(attached.Select(entry => entry.Value)).IsEquivalentTo(new Exception[] { readerFailure, callbackFailure });
            await Assert.That(attached.Select(entry => entry.Key)).IsEquivalentTo(["event reader", "event cancellation"]);
        }
        finally
        {
            release.Set();
            _ = pending.TrySetResult(true);
        }
    }

    [Test]
    public async Task CompleteAsync_Should_Accept_Only_Its_Own_Cooperative_Reader_Cancellation()
    {
        var owner = new OwnedEventReader(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), CancellationToken.None);
        var pending = owner.Start(async token =>
        {
            var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => _ = cancellation.TrySetCanceled(token));
            _ = await cancellation.Task;
        });

        await owner.CompleteAsync(null);

        OperationCanceledException? cancelled = null;
        try
        {
            await pending;
        }
        catch (OperationCanceledException exception)
        {
            cancelled = exception;
        }

        await Assert.That(cancelled).IsNotNull();
        await Assert.That(owner.LateFailures).IsNull();
    }

    [Test]
    public async Task CompleteAsync_Should_Preserve_Caller_Cancellation_Identity()
    {
        using var caller = new CancellationTokenSource();
        var owner = new OwnedEventReader(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), caller.Token);
        await caller.CancelAsync();
        var primary = new OperationCanceledException(caller.Token);
        _ = owner.Start(_ => Task.FromException(primary));

        var thrown = await Assert.That(() => owner.CompleteAsync(primary))
            .Throws<OperationCanceledException>();

        await Assert.That(thrown).IsSameReferenceAs(primary);
        await Assert.That(thrown!.CancellationToken).IsEqualTo(caller.Token);
        await Assert.That(primary.Data.Contains(OwnedCleanup.FailuresKey)).IsFalse();
    }

    [Test]
    public async Task CompleteAsync_Should_Report_Unexpected_Reader_Cancellation()
    {
        var owner = new OwnedEventReader(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), CancellationToken.None);
        var unexpected = new OperationCanceledException("unrelated cancellation");
        _ = owner.Start(_ => Task.FromException(unexpected));
        var primary = new InvalidOperationException("prompt failed");

        var thrown = await Assert.That(() => owner.CompleteAsync(primary))
            .Throws<InvalidOperationException>();

        await Assert.That(thrown).IsSameReferenceAs(primary);
        var failures = (AggregateException)primary.Data[OwnedCleanup.FailuresKey]!;
        await Assert.That(failures.InnerExceptions.Single()).IsSameReferenceAs(unexpected);
    }

    [Test]
    public async Task CompleteAsync_Should_Observe_Late_Cooperative_Cancellation_After_The_Reader_Deadline()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new OwnedEventReader(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(50), CancellationToken.None);
        owner.Own(pending.Task);
        var token = owner.Token;
        try
        {
            _ = await Assert.That(() => owner.CompleteAsync(null)).Throws<TimeoutException>();
            var report = owner.LateFailures;
            await Assert.That(report).IsNotNull();

            pending.SetCanceled(token);
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await report.WaitForAllAsync(observation.Token);

            await Assert.That(report.Failures).IsEmpty();
        }
        finally
        {
            _ = pending.TrySetResult(true);
        }
    }
}
