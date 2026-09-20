using System.Runtime.InteropServices;

namespace OpenCode.Sdk.ServiceFixture;

/// <summary>
/// A process for the stop tests to end: prints <c>ready</c> once its stdout is open, then lingers
/// until a signal ends it. With <c>ignoreTerminate</c> it registers a <c>SIGTERM</c> handler that
/// cancels the runtime's default exit, so only the kill rung can end it — the shape of a daemon
/// that survives the first rung. Windows cannot deliver a <c>SIGTERM</c> to another process, so
/// the handler is not registered there and a hard kill ends the process on the first rung.
/// </summary>
internal static class LingeringProcessMode
{
    public static async Task<int> RunAsync(bool ignoreTerminate)
    {
        using var registration = ignoreTerminate && !OperatingSystem.IsWindows()
            ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, static context => context.Cancel = true)
            : null;
        await Console.Out.WriteLineAsync("ready").ConfigureAwait(false);
        await Console.Out.FlushAsync().ConfigureAwait(false);

        // A gate nobody opens: the process lingers until a signal ends it, which is the point.
        using var forever = new SemaphoreSlim(0);
        await forever.WaitAsync().ConfigureAwait(false);
        return 0;
    }
}
