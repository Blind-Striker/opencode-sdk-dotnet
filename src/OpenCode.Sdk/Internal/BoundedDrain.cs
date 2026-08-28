namespace OpenCode.Sdk.Internal;

/// <summary>
/// Runs a blocking, diagnostics-only action with a hard timeout so a best-effort drain can never
/// turn into an unbounded hang. A timeout is swallowed rather than surfaced: the caller proceeds
/// with whatever was already captured, because on a failure path promptness always outranks
/// completeness. There is no cancellable overload of
/// <see cref="System.Diagnostics.Process.WaitForExit()"/> that also drains the redirected output
/// streams, so the action keeps running to whatever end it reaches on its own thread-pool thread
/// even after a timeout — the accepted cost of bounding it this way.
/// </summary>
internal static class BoundedDrain
{
    public static async Task RunAsync(Action action, TimeSpan timeout)
    {
        try
        {
            await Task.Run(action).WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Best-effort only: proceed with whatever was captured before the bound expired.
        }
    }
}
