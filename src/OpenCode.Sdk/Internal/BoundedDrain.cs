namespace OpenCode.Sdk.Internal;

/// <summary>
/// Runs a blocking, diagnostics-only drain with a hard timeout so a best-effort drain can never
/// turn into an unbounded hang. An expired bound is reported as an incomplete drain rather than
/// raised: the caller proceeds with whatever was already captured, because on a failure path
/// promptness always outranks completeness. There is no cancellable overload of
/// <see cref="System.Diagnostics.Process.WaitForExit()"/> that also drains the redirected output
/// streams, so the drain keeps running to whatever end it reaches on its own thread-pool thread
/// even after a timeout — the accepted cost of bounding it this way.
/// </summary>
internal static class BoundedDrain
{
    /// <summary>Runs <paramref name="drain"/> on the thread pool under a hard bound.</summary>
    /// <param name="drain">The blocking drain; reports whether it finished its own work.</param>
    /// <param name="timeout">The bound the drain gets before the caller stops waiting for it.</param>
    /// <returns>
    /// True when the drain finished inside the bound; false when the bound expired or the drain
    /// itself reported that it could not finish.
    /// </returns>
    public static async Task<bool> RunAsync(Func<bool> drain, TimeSpan timeout)
    {
        try
        {
            return await Task.Run(drain).WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The bound expired with the drain still blocked. Reporting the incompleteness is all
            // this class can honestly do: the caller keeps whatever was already captured, and the
            // abandoned drain finishes (or does not) on its own thread-pool thread.
            return false;
        }
    }
}
