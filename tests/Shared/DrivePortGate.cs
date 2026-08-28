using System.Globalization;
using System.IO.Abstractions;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Serializes the drive manifest's reserve-then-rebind window across every test host on the
/// machine. <see cref="LoopbackPortReservation"/> must release a port before the pinned server
/// can bind it, and that server takes seconds to boot, so two hosts starting simulated servers at
/// the same moment are handed the same loopback port and the loser dies before readiness with
/// "Failed to start server. Is port N in use?" (control-server.ts:61) - reproduced on this
/// repository's own four-target-framework run, where the four legs start in the same second.
/// Holding this gate from the reservation until the backend control socket is proven bound closes
/// the window by construction rather than by odds; once the winner's server owns the port, no
/// later reservation can be handed it.
/// A lock file rather than a <see cref="Mutex"/>: a mutex has thread affinity and this is held
/// across awaits, and the operating system releases a file lock when a killed holder's process
/// exits, so a crashed run cannot wedge the next one.
/// </summary>
internal sealed class DrivePortGate : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly FileSystemStream _handle;

    private DrivePortGate(FileSystemStream handle) => _handle = handle;

    public static Task<DrivePortGate> AcquireAsync(IFileSystem fileSystem, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var directory = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "opencode-sdk-tests");
        _ = fileSystem.Directory.CreateDirectory(directory);
        return AcquireAsync(fileSystem, fileSystem.Path.Combine(directory, "drive-ports.lock"), timeout);
    }

    /// <summary>
    /// Test-only seam: the shared gate path above is the one every fixture on the machine
    /// contends for, so a test that proved mutual exclusion against it would stall real fixtures.
    /// Taking the path lets the contract be pinned against a private file instead.
    /// </summary>
    [SlopwatchSuppress(
        "SW004",
        "Test-only cross-process gate: the drive manifest requires a pre-picked port (upstream forbids port 0) that the child binds later in another process, and no OS API signals another process's file-lock release — polling the lock is the condition wait.")]
    public static async Task<DrivePortGate> AcquireAsync(IFileSystem fileSystem, string path, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var handle = TryOpenExclusively(fileSystem, path);
            if (handle is not null)
            {
                return new DrivePortGate(handle);
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Another test host held the drive port gate at '{path}' for more than {timeout.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} minutes.");
            }

            // The gate is an operating-system file lock held by another *process*, and no portable
            // API signals its release: a named mutex or semaphore is what the class comment above
            // already rules out, and a watcher never fires because the file is unlocked, not
            // changed. Polling the lock is therefore the condition wait, and this is its interval.
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>
    /// Takes the gate file for exclusive use.
    /// </summary>
    /// <returns>
    /// The held handle, or null when another host holds the gate — which is contention, not
    /// failure, so the caller's bounded poll turns it into either the gate or a loud timeout.
    /// </returns>
    private static FileSystemStream? TryOpenExclusively(IFileSystem fileSystem, string path)
    {
        try
        {
            return fileSystem.FileStream.New(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // A sharing violation: another host holds the gate.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // The same contention surfaces as an access denial on some platforms.
            return null;
        }
    }

    public void Dispose() => _handle.Dispose();
}
