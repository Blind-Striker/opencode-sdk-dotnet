using System.Runtime.ExceptionServices;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class OwnedCleanup(TimeSpan timeout)
{
    internal const string FailuresKey = "OwnedSessionCleanup.Failures";
    internal const string LateFailuresKey = "OwnedSessionCleanup.LateFailures";
    private readonly LateCleanupFailureReport _lateFailures = new();
    private readonly List<OwnedCleanupOperation> _operations = [];

    public LateCleanupFailureReport? LateFailures =>
        _lateFailures.HasRegistrations ? _lateFailures : null;

    public void Own(string name, Func<CancellationToken, Task> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(operation);
        _operations.Add(new OwnedCleanupOperation(name, operation));
    }

    public void Own(string name, Task pending, Func<OperationCanceledException, bool>? expectedCancellation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(pending);
        _operations.Add(new OwnedCleanupOperation(name, pending, expectedCancellation));
    }

    public async Task CompleteAsync(Exception? primaryFailure)
    {
        _lateFailures.InheritDiagnostics(primaryFailure);
        var failures = new List<Exception>();
        foreach (var operation in _operations)
        {
            await CaptureFailureAsync(operation, failures);
        }

        failures.RemoveAll(failure => ReferenceEquals(failure, primaryFailure));
        ThrowFailures(primaryFailure, failures);
    }

    private async Task CaptureFailureAsync(
        OwnedCleanupOperation operation,
        List<Exception> failures)
    {
        CancellationTokenSource? budget = new();
        try
        {
            var token = budget.Token;
            // Invocation itself can block (including a synchronous cancellation callback).
            // The deadline must not depend on that work or on its cooperative token.
            var pending = await ObserveAsync(operation.Name, () => operation.StartAsync(token),
                failures, operation.ExpectedCancellation);
            if (pending.IsCompleted)
            {
                return;
            }

            var cancellation = await ObserveAsync(operation.Name + " cancellation", budget.CancelAsync, failures);
            DisposeBudgetAfterCompletion(budget, pending, cancellation);
            budget = null;
        }
        finally
        {
            budget?.Dispose();
        }
    }

    private static void DisposeBudgetAfterCompletion(CancellationTokenSource budget, Task pending, Task cancellation)
    {
        _ = Task.WhenAll(pending, cancellation).ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                budget.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<Task> ObserveAsync(string name, Func<Task> start, List<Exception> failures,
        Func<OperationCanceledException, bool>? expectedCancellation = null)
    {
        var pending = Task.Run(start, CancellationToken.None);
        try
        {
            await pending.WaitAsync(timeout);
        }
        catch (OperationCanceledException exception) when (expectedCancellation?.Invoke(exception) is true)
        {
            // An owned reader can identify its own cooperative teardown cancellation.
            return pending;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            if (!pending.IsCompleted)
            {
                _lateFailures.Observe(name, pending, expectedCancellation);
            }
            else if (pending.Exception is { } aggregate)
            {
                foreach (var failure in aggregate.Flatten().InnerExceptions)
                {
                    if (!ReferenceEquals(failure, exception)
                        && (exception is not AggregateException caught
                            || !caught.Flatten().InnerExceptions.Contains(failure)))
                    {
                        failures.Add(failure);
                    }
                }
            }
        }

        return pending;
    }

    private void ThrowFailures(Exception? primaryFailure, List<Exception> failures)
    {
        if (primaryFailure is not null)
        {
            if (failures.Count > 0)
            {
                var previous = primaryFailure.Data[FailuresKey] as AggregateException;
                primaryFailure.Data[FailuresKey] = new AggregateException(
                    (previous?.InnerExceptions.AsEnumerable() ?? []).Concat(failures));
            }

            AttachLateFailures(primaryFailure);
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (failures.Count is 1)
        {
            AttachLateFailures(failures[0]);
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            var aggregate = new AggregateException("Multiple failures occurred during owned cleanup.", failures);
            AttachLateFailures(aggregate);
            throw aggregate;
        }
    }

    private void AttachLateFailures(Exception exception)
    {
        if (_lateFailures.HasRegistrations)
        {
            exception.Data[LateFailuresKey] = _lateFailures.DiagnosticFailures;
        }
    }
}
