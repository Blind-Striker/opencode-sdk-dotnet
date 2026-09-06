using System.Collections.Concurrent;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class LateCleanupFailureReport
{
    private readonly ConcurrentQueue<KeyValuePair<string, Exception>> _failures = new();
    private readonly TaskCompletionSource<KeyValuePair<string, Exception>> _firstFailure = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _registrations;

    public bool HasRegistrations => Volatile.Read(ref _registrations) > 0;

    public IReadOnlyList<KeyValuePair<string, Exception>> Failures => [.. _failures];

    public void Observe(string name, Task operation)
    {
        _ = Interlocked.Increment(ref _registrations);
        _ = operation.ContinueWith(
            completed =>
            {
                foreach (var failure in completed.Exception!.Flatten().InnerExceptions)
                {
                    var entry = new KeyValuePair<string, Exception>(name, failure);
                    _failures.Enqueue(entry);
                    _ = _firstFailure.TrySetResult(entry);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public async Task<KeyValuePair<string, Exception>> WaitForFailureAsync(CancellationToken cancellationToken) =>
        await _firstFailure.Task.WaitAsync(cancellationToken);
}
