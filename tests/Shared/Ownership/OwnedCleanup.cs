using System.Runtime.ExceptionServices;
using OpenCode.Sdk.TestSupport.Ownership.Abstractions;

namespace OpenCode.Sdk.TestSupport.Ownership;

internal sealed class OwnedCleanup(TimeSpan timeout, IOwnedOperationDeadline deadline)
{
    internal const string FailuresKey = "OwnedSessionCleanup.Failures";
    internal const string LateFailuresKey = "OwnedSessionCleanup.LateFailures";
    private readonly LateCleanupFailureReport _lateFailures = new();
    private readonly List<OwnedCleanupOperation> _operations = [];

    public OwnedCleanup(TimeSpan timeout) : this(timeout, new OwnedOperationDeadline())
    {
    }

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
            var pending = operation.StartAsync(token);
            await ObserveAsync(operation.Name, pending, failures, operation.ExpectedCancellation);
            if (pending.IsCompleted)
            {
                return;
            }

            var cancellation = Task.Run(budget.CancelAsync, CancellationToken.None);
            await ObserveAsync(operation.Name + " cancellation", cancellation, failures);
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

    private async Task ObserveAsync(string name, Task pending, List<Exception> failures,
        Func<OperationCanceledException, bool>? expectedCancellation = null)
    {
        try
        {
            await deadline.WaitAsync(name, pending, timeout);
        }
        catch (TimeoutException exception)
        {
            failures.Add(new TimeoutException($"Owned operation '{name}' exceeded its cleanup deadline of {timeout}.", exception));
            // The task may already be terminal when the deadline result reaches us.
            // Always observe it once, including cancellation with no Task.Exception.
            _lateFailures.Observe(name, pending, expectedCancellation);
            return;
        }

        try
        {
            await pending;
        }
        catch (OperationCanceledException exception) when (expectedCancellation?.Invoke(exception) is true)
        {
            // An owned reader can identify its own cooperative teardown cancellation.
            return;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            if (pending.Exception is { } aggregate)
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
