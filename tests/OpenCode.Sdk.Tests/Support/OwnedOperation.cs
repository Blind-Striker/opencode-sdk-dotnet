namespace OpenCode.Sdk.Tests.Support;

internal sealed class OwnedOperation<T>
{
    private readonly Func<Task> _cancel;
    private readonly Task<T> _completion;
    private readonly TaskCompletionSource<Exception?> _lateObservation = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _observed;
    private int _observerRegistered;

    public OwnedOperation(Func<Task<T>> start, Func<Task> cancel)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(cancel);
        _cancel = cancel;
        _completion = start();
    }

    public async Task<T> CompleteAsync()
    {
        try
        {
            return await _completion;
        }
        finally
        {
            _ = Interlocked.Exchange(ref _observed, 1);
        }
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _observed) is 1)
        {
            return;
        }

        try
        {
            if (!_completion.IsCompleted)
            {
                await _cancel().WaitAsync(cancellationToken);
            }

            _ = await _completion.WaitAsync(cancellationToken);
            _ = Interlocked.Exchange(ref _observed, 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveLateFault();
            throw;
        }
        catch
        {
            if (_completion.IsCompleted)
            {
                _ = Interlocked.Exchange(ref _observed, 1);
            }
            else
            {
                ObserveLateFault();
            }

            throw;
        }
    }

    internal async Task<Exception?> WaitForLateObservationAsync(CancellationToken cancellationToken) =>
        await _lateObservation.Task.WaitAsync(cancellationToken);

    private void ObserveLateFault()
    {
        if (Interlocked.Exchange(ref _observerRegistered, 1) is 1)
        {
            return;
        }

        _ = _completion.ContinueWith(
            completed =>
            {
                var aggregate = completed.Exception;
                var failure = aggregate?.InnerExceptions.Count is 1
                    ? aggregate.InnerExceptions[0]
                    : aggregate;
                _ = _lateObservation.TrySetResult(failure);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
