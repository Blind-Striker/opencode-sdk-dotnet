using System.Runtime.ExceptionServices;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class OwnedSessionCleanup
{
    internal const string FailuresKey = "OwnedSessionCleanup.Failures";
    internal const string LateFailuresKey = "OwnedSessionCleanup.LateFailures";

    private readonly Func<CancellationToken, Task> _interrupt;
    private readonly Func<CancellationToken, Task> _remove;
    private readonly LateCleanupFailureReport _lateFailures = new();
    private readonly List<KeyValuePair<string, Func<CancellationToken, Task>>> _resources = [];
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

    public void Own(string name, Func<CancellationToken, Task> resourceCleanup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resourceCleanup);
        _resources.Add(new KeyValuePair<string, Func<CancellationToken, Task>>(name, resourceCleanup));
    }

    public async Task CompleteAsync(Exception? primaryFailure)
    {
        var failures = new List<Exception>();
        foreach (var cleanup in _resources)
        {
            await CaptureFailureAsync(cleanup.Key, cleanup.Value, failures);
        }

        if (!_turnCompleted)
        {
            await CaptureFailureAsync("session interrupt", _interrupt, failures);
        }

        await CaptureFailureAsync("session removal", _remove, failures);
        ThrowFailures(primaryFailure, failures);
    }

    private async Task CaptureFailureAsync(
        string name,
        Func<CancellationToken, Task> operation,
        List<Exception> failures)
    {
        using var budget = new CancellationTokenSource(_timeout);
        Task? pending = null;
        try
        {
            pending = operation(budget.Token);
            await pending.WaitAsync(budget.Token);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            if (pending is { IsCompleted: false })
            {
                _lateFailures.Observe(name, pending);
            }
            else if (pending?.Exception is { } aggregate)
            {
                foreach (var failure in aggregate.Flatten().InnerExceptions)
                {
                    if (!ReferenceEquals(failure, exception))
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
                primaryFailure.Data[FailuresKey] = new AggregateException(failures);
            }

            AttachLateFailureReport(primaryFailure);
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (failures.Count is 1)
        {
            AttachLateFailureReport(failures[0]);
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            var aggregate = new AggregateException("Multiple failures occurred while cleaning the session.", failures);
            AttachLateFailureReport(aggregate);
            throw aggregate;
        }
    }

    private void AttachLateFailureReport(Exception exception)
    {
        if (_lateFailures.HasRegistrations)
        {
            exception.Data[LateFailuresKey] = _lateFailures;
        }
    }
}
