using System.ComponentModel;
using System.Diagnostics;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// The pump outside Windows: <see cref="Process"/>'s own event readers, whose pipe reads are
/// asynchronous there and hold no thread while they wait.
/// </summary>
internal sealed class ProcessEventOutputPump : ChildOutputPump
{
    private readonly Process _process;

    private ProcessEventOutputPump(Process process) => _process = process;

    /// <summary>Attaches the line handlers and begins both asynchronous reads.</summary>
    /// <param name="process">The started child.</param>
    /// <param name="onStandardOutput">Receives each stdout line.</param>
    /// <param name="onStandardError">Receives each stderr line.</param>
    /// <returns>The running pump.</returns>
    public static ProcessEventOutputPump Begin(Process process, Action<string> onStandardOutput, Action<string> onStandardError)
    {
        process.OutputDataReceived += (_, received) =>
        {
            if (received.Data is not null)
            {
                onStandardOutput(received.Data);
            }
        };
        process.ErrorDataReceived += (_, received) =>
        {
            if (received.Data is not null)
            {
                onStandardError(received.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new ProcessEventOutputPump(process);
    }

    /// <summary>The event readers hold no thread outside Windows, so none is ever outstanding.</summary>
    /// <inheritdoc />
    public override Task ReadersEnded => Task.CompletedTask;

    /// <summary>
    /// Drains through the parameterless <see cref="Process.WaitForExit()"/>, the only wait that
    /// also waits for the event readers to reach end-of-stream — Polyfill's downlevel
    /// <c>WaitForExitAsync</c> is Exited-event-only and the timeout overload never drains — under
    /// <see cref="BoundedDrain"/>'s hard bound.
    /// </summary>
    /// <inheritdoc />
    public override Task<bool> DrainAsync(TimeSpan bound) =>
        BoundedDrain.RunAsync(WaitForExitBestEffort, bound);

    /// <summary>The event readers hold no thread outside Windows; disposing the process ends them.</summary>
    /// <inheritdoc />
    public override Task ReleaseAsync() => Task.CompletedTask;

    private bool WaitForExitBestEffort()
    {
        try
        {
            _process.WaitForExit();
            return true;
        }
        catch (InvalidOperationException)
        {
            // No process handle left to drain; nothing this call can still flush.
            return false;
        }
        catch (Win32Exception)
        {
            // The handle is gone or inaccessible, so the readers were never drained here.
            return false;
        }
    }
}
