using System.Runtime.ExceptionServices;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class OwnedSessionCleanup
{
    internal const string FailuresKey = "OwnedSessionCleanup.Failures";

    private readonly Func<CancellationToken, Task> _interrupt;
    private readonly Func<CancellationToken, Task> _remove;
    private readonly List<Func<CancellationToken, Task>> _resources = [];
    private readonly TimeSpan _timeout;
    private bool _turnCompleted;

    public OwnedSessionCleanup(SessionClient session, TimeSpan timeout)
        : this(
            async token =>
            {
                _ = await session.PostInterruptAsync(cancellationToken: token);
            },
            async token =>
            {
                _ = await session.RemoveSessionAsync(cancellationToken: token);
            },
            timeout)
    {
        ArgumentNullException.ThrowIfNull(session);
    }

    internal OwnedSessionCleanup(
        Func<CancellationToken, Task> interrupt,
        Func<CancellationToken, Task> remove,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(interrupt);
        ArgumentNullException.ThrowIfNull(remove);
        _interrupt = interrupt;
        _remove = remove;
        _timeout = timeout;
    }

    public void MarkTurnCompleted() => _turnCompleted = true;

    public void MarkTurnStarted() => _turnCompleted = false;

    public void Own(Func<CancellationToken, Task> resourceCleanup)
    {
        ArgumentNullException.ThrowIfNull(resourceCleanup);
        _resources.Add(resourceCleanup);
    }

    public OwnedOperation<T> Own<T>(Func<Task<T>> start, Func<Task> cancel)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(cancel);
        var operation = new OwnedOperation<T>(start, cancel);
        _resources.Add(operation.CleanupAsync);
        return operation;
    }

    public async Task CompleteAsync(Exception? primaryFailure)
    {
        var failures = new List<Exception>();
        foreach (var cleanup in _resources)
        {
            await CaptureFailureAsync(cleanup, failures);
        }

        if (!_turnCompleted)
        {
            await CaptureFailureAsync(_interrupt, failures);
        }

        await CaptureFailureAsync(_remove, failures);
        ThrowFailures(primaryFailure, failures);
    }

    private async Task CaptureFailureAsync(
        Func<CancellationToken, Task> operation,
        List<Exception> failures)
    {
        using var budget = new CancellationTokenSource(_timeout);
        try
        {
            await operation(budget.Token);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void ThrowFailures(Exception? primaryFailure, List<Exception> failures)
    {
        if (primaryFailure is not null)
        {
            if (failures.Count > 0)
            {
                primaryFailure.Data[FailuresKey] = new AggregateException(failures);
            }

            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (failures.Count is 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("Multiple failures occurred while cleaning the session.", failures);
        }
    }
}
