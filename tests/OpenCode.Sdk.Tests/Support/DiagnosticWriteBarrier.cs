namespace OpenCode.Sdk.Tests.Support;

/// <summary>Controls a synchronous filesystem callback executed by the owned capture worker.</summary>
internal sealed class DiagnosticWriteBarrier : IDisposable
{
    private readonly ManualResetEventSlim _release = new();
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;

    public void Block()
    {
        _ = _entered.TrySetResult(true);
        _release.Wait();
    }

    public void Release() => _release.Set();

    public void Dispose() => _release.Dispose();
}
