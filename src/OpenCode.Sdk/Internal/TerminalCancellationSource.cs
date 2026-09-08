namespace OpenCode.Sdk.Internal;

/// <summary>Owns terminal cancellation without blocking a worker while callbacks are scheduled.</summary>
internal sealed class TerminalCancellationSource : IDisposable
{
    private readonly CancellationTokenSource _source = new();

    /// <summary>Gets the token observed by owned I/O or send admission.</summary>
    public CancellationToken Token => _source.Token;

    /// <summary>Requests cancellation and observes all callbacks before completion.</summary>
    public Task CancelAsync()
    {
#if NET8_0_OR_GREATER
        return _source.CancelAsync();
#else
        // Polyfill 10.3's CancelAsync busy-waits for a queued Task.Run to start. Concurrent
        // terminal shutdown can occupy every worker with that spin and starve cancellation.
        // Our lifetime owner prevents source disposal until this task and owned I/O finish.
        return Task.Run(_source.Cancel, CancellationToken.None);
#endif
    }

    /// <summary>Releases the source after cancellation callbacks and owned I/O have settled.</summary>
    public void Dispose() => _source.Dispose();
}
