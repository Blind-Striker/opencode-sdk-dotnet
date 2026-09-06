using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

public sealed class SessionLogTranscriptTests
{
    [Test]
    public async Task Cleanup_Should_Bound_Stalled_Disposal_And_Retain_Its_Late_Failure()
    {
        var disposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerator = new ControlledAsyncEnumerator(disposal.Task);
        using var window = new CancellationTokenSource();
        var transcript = new SessionLogTranscript(enumerator, window);
        var primaryFailure = new InvalidOperationException("primary failure");
        var removalFailure = new InvalidOperationException("removal failure");
        var lateDisposalFailure = new InvalidOperationException("late disposal failure");
        var removalTokenWasCancelled = true;
        var cleanup = new OwnedSessionCleanup(
            _ => Task.CompletedTask,
            token =>
            {
                removalTokenWasCancelled = token.IsCancellationRequested;
                return Task.FromException(removalFailure);
            },
            TimeSpan.FromMilliseconds(50));
        cleanup.MarkTurnCompleted();
        cleanup.Own("session log enumerator", transcript.DisposeAsync);

        var thrown = await Assert.That(async () => await cleanup.CompleteAsync(primaryFailure))
            .Throws<InvalidOperationException>();

        await Assert.That(thrown).IsSameReferenceAs(primaryFailure);
        await Assert.That(enumerator.DisposeCalls).IsEqualTo(1);
        await Assert.That(removalTokenWasCancelled).IsFalse();
        var failures = primaryFailure.Data[OwnedSessionCleanup.FailuresKey] as AggregateException;
        await Assert.That(failures).IsNotNull();
        await Assert.That(failures!.InnerExceptions).Count().IsEqualTo(2);
        await Assert.That(failures.InnerExceptions[0]).IsTypeOf<OperationCanceledException>();
        await Assert.That(failures.InnerExceptions[1]).IsSameReferenceAs(removalFailure);
        var diagnosticFailures = primaryFailure.Data[OwnedSessionCleanup.LateFailuresKey]
            as IReadOnlyCollection<KeyValuePair<string, Exception>>;
        await Assert.That(diagnosticFailures).IsNotNull();
        var late = cleanup.LateFailures;
        await Assert.That(late).IsNotNull();

        disposal.SetException(lateDisposalFailure);
        using var observationBudget = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var observed = await late.WaitForFailureAsync(observationBudget.Token);

        await Assert.That(observed.Key).IsEqualTo("session log enumerator");
        await Assert.That(observed.Value).IsSameReferenceAs(lateDisposalFailure);
        await Assert.That(late.Failures).Count().IsEqualTo(1);
        await Assert.That(late.Failures[0].Value).IsSameReferenceAs(lateDisposalFailure);
        await Assert.That(diagnosticFailures!).Count().IsEqualTo(1);
        await Assert.That(diagnosticFailures!.Single().Key).IsEqualTo("session log enumerator");
        await Assert.That(diagnosticFailures.Single().Value).IsSameReferenceAs(lateDisposalFailure);
        _ = await Assert.That(window.Cancel).Throws<ObjectDisposedException>();
        Console.WriteLine(
            "session-log-cleanup: immediate-failures=" + failures.InnerExceptions.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture) +
            " removal-token-cancelled=" + removalTokenWasCancelled.ToString() +
            " late-operation=" + observed.Key);
    }

    private sealed class ControlledAsyncEnumerator(Task disposal) : IAsyncEnumerator<ISessionLogItem>
    {
        public ISessionLogItem Current => throw new InvalidOperationException("No item is available.");

        public int DisposeCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return new ValueTask(disposal);
        }

        public ValueTask<bool> MoveNextAsync() => new(false);
    }
}
