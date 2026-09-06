using OpenCode.Sdk.Tests.Support.Abstractions;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class OwnedOperationDeadline : IOwnedOperationDeadline
{
    public async Task WaitAsync(string name, Task operation, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        // Observe terminal state separately from the outcome: the operation can itself time out.
        var completion = operation.ContinueWith(
            static _ => true, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = await completion.WaitAsync(timeout, cancellationToken);
    }
}
