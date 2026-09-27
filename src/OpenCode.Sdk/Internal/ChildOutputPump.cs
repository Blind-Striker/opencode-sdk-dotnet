using System.Diagnostics;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Reads a launched child's redirected stdout and stderr continuously and releases the readers.
/// On Windows each stream is read on a dedicated thread (<see cref="DedicatedThreadOutputPump"/>),
/// because <see cref="Process"/>'s pipes there are synchronous before .NET 11 and its event
/// readers would hold two thread-pool threads for the server's whole life; elsewhere the pipe
/// reads are truly asynchronous and the event readers hold no thread
/// (<see cref="ProcessEventOutputPump"/>).
/// </summary>
internal abstract class ChildOutputPump
{
    /// <summary>Starts reading both redirected streams of an already started process.</summary>
    /// <param name="process">The started child, with stdout and stderr redirected.</param>
    /// <param name="onStandardOutput">Receives each stdout line.</param>
    /// <param name="onStandardError">Receives each stderr line.</param>
    /// <returns>The running pump; its owner releases it before disposing the process.</returns>
    public static ChildOutputPump Start(Process process, Action<string> onStandardOutput, Action<string> onStandardError) =>
        LauncherInterop.IsWindows
            ? DedicatedThreadOutputPump.Begin(process, onStandardOutput, onStandardError)
            : ProcessEventOutputPump.Begin(process, onStandardOutput, onStandardError);

    /// <summary>
    /// Gets a task that completes once no reader of this pump holds a thread any more; what
    /// disposal's release guarantees, and what a test observes to prove it.
    /// </summary>
    public abstract Task ReadersEnded { get; }

    /// <summary>
    /// Waits, inside <paramref name="bound"/>, for both streams to reach end-of-stream, so every
    /// line the child wrote has been delivered. End-of-stream arrives only when every process
    /// holding a write end has closed it, which a surviving descendant can prevent: the bound is
    /// the guarantee, and an expired bound is reported rather than raised.
    /// </summary>
    /// <param name="bound">How long the caller waits for end-of-stream.</param>
    /// <returns>True when both streams reached end-of-stream inside the bound.</returns>
    public abstract Task<bool> DrainAsync(TimeSpan bound);

    /// <summary>
    /// Ends every reader this pump started, whether or not its stream reached end-of-stream.
    /// After it completes no reader holds a thread; the process is disposed only after this.
    /// </summary>
    /// <returns>A task that completes once the readers are released.</returns>
    public abstract Task ReleaseAsync();
}
