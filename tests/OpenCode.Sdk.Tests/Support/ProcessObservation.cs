namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Process-truth observation for launcher tests: whether a process identifier is still alive, and
/// a bounded wait for it to stop being. An absent process is the state these wait for, so a
/// process that is already gone answers immediately rather than failing the arrangement.
/// </summary>
internal static class ProcessObservation
{
    public static bool IsRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static async Task WaitForExitAsync(int processId, CancellationToken cancellationToken)
    {
        System.Diagnostics.Process child;
        try
        {
            child = System.Diagnostics.Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (child)
        {
            await child.WaitForExitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Waits at most <paramref name="bound"/> for the process to exit. A whole-tree kill is
    /// asynchronous, so a descendant is observed to terminate inside a bound of the test's own
    /// rather than asserted gone at the instant disposal returns.
    /// </summary>
    /// <returns>True when the process exited inside the bound.</returns>
    public static async Task<bool> ObserveExitWithinAsync(
        int processId, TimeSpan bound, CancellationToken cancellationToken)
    {
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        observation.CancelAfter(bound);
        try
        {
            await WaitForExitAsync(processId, observation.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
