namespace OpenCode.Sdk.Tests.Support;

internal sealed class OwnedEventReader
{
    private readonly CancellationToken _callerToken;
    private readonly CancellationTokenSource _window;
    private readonly CancellationToken _eventToken;
    private readonly OwnedCleanup _cleanup;
    private readonly TimeSpan _observationWindow;
    private bool _teardownCancellation;
    private Task _reader = Task.CompletedTask;
    private Task _cancellation = Task.CompletedTask;

    public OwnedEventReader(TimeSpan observationWindow, TimeSpan cleanupTimeout, CancellationToken callerToken)
    {
        _callerToken = callerToken;
        _observationWindow = observationWindow;
        _window = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        _eventToken = _window.Token;
        _window.CancelAfter(observationWindow);
        _cleanup = new OwnedCleanup(cleanupTimeout);
    }

    public CancellationToken Token => _eventToken;

    public LateCleanupFailureReport? LateFailures => _cleanup.LateFailures;

    public bool IsTeardownCancellation(OperationCanceledException exception) =>
        _teardownCancellation && !_callerToken.IsCancellationRequested
        && exception.CancellationToken == Token && Token.IsCancellationRequested;

    public bool ObservationTimedOut(OperationCanceledException exception) =>
        !_callerToken.IsCancellationRequested && Token.IsCancellationRequested
        && !IsTeardownCancellation(exception);

    public Task<T> Start<T>(Func<CancellationToken, Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var pending = Task.Run(() => read(Token), CancellationToken.None);
        _reader = pending;
        return pending.WaitAsync(_observationWindow, _callerToken);
    }

    public Task Start(Func<CancellationToken, Task> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var pending = Task.Run(() => read(Token), CancellationToken.None);
        _reader = pending;
        return pending.WaitAsync(_observationWindow, _callerToken);
    }

    public void Own(Task reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public async Task CompleteAsync(Exception? primaryFailure)
    {
        _teardownCancellation = !_window.IsCancellationRequested;
        _cancellation = Task.Run(_window.CancelAsync, CancellationToken.None);
        _cleanup.Own("event cancellation", _cancellation);
        _cleanup.Own("event reader", _reader, IsTeardownCancellation);
        try
        {
            await _cleanup.CompleteAsync(primaryFailure);
        }
        finally
        {
            // Retain the window while either callbacks or reader disposal still use it.
            _ = Task.WhenAll(_cancellation, _reader).ContinueWith(
                completed =>
                {
                    _ = completed.Exception;
                    _window.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

}
