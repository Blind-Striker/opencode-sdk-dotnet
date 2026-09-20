using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The shipped <see cref="IServiceProcessControl"/> on the platform's own primitives (ADR-0026).
/// Identity is the pid with the start time the operating system recorded for it
/// (<c>GetProcessTimes</c>, <c>/proc/&lt;pid&gt;/stat</c>, and <c>proc_pidinfo</c> behind
/// <see cref="Process.StartTime"/>), the token Aspire and psutil use, because a pid alone is reused
/// and <see cref="Process.Kill()"/> verifies nothing about a process this one did not start. On Unix
/// the rungs are <c>SIGTERM</c> and <c>SIGKILL</c> through <c>kill(2)</c>: .NET's public surface
/// sends only <c>SIGKILL</c>, and both the pinned client's runtime and .NET's own <c>Kill</c> reach
/// the same call one layer down. On Windows another process cannot be signalled, so both rungs are
/// <see cref="Process.Kill()"/> (<c>TerminateProcess</c>), which is what libuv does with a
/// <c>SIGTERM</c> there.
/// </summary>
internal sealed partial class ServiceProcessControl : IServiceProcessControl
{
    private const int Sigterm = 15;
    private const int Sigkill = 9;

    public ProcessIdentity? TrySnapshot(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessIdentity(processId, process.StartTime.ToUniversalTime());
        }
        catch (ArgumentException)
        {
            // No process runs under the pid.
            return null;
        }
        catch (InvalidOperationException)
        {
            // It exited between the lookup and the read.
            return null;
        }
        catch (Win32Exception)
        {
            // The platform refused the read (another user's process, a vanished /proc entry): the
            // pinned client reads a refused kill(pid, 0) the same way, as a process that is gone.
            return null;
        }
    }

    public bool TrySignal(ProcessIdentity target, ProcessSignal signal)
    {
        if (TrySnapshot(target.ProcessId) != target)
        {
            return false;
        }

        return IsWindows ? TryTerminateProcess(target.ProcessId) : TrySendSignal(target.ProcessId, signal);
    }

    private static bool TryTerminateProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool TrySendSignal(int processId, ProcessSignal signal) =>
        Kill(processId, signal == ProcessSignal.Terminate ? Sigterm : Sigkill) == 0;

    private static bool IsWindows =>
#if NET
        OperatingSystem.IsWindows();
#else
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif

    /// <summary>
    /// <c>kill(2)</c>: two blittable integers, so there is no marshalling to generate on either
    /// form. The modern targets take <c>LibraryImport</c>, the compile-time stub .NET recommends
    /// (its generator is what needs the project's unsafe-code switch); the <c>netstandard2.0</c>
    /// asset, where that generator is unavailable, takes the equivalent <c>DllImport</c>. "libc" is
    /// the portable spelling: <c>libSystem.Native</c>'s loader maps it to the platform's own C
    /// library (<c>libc.so.6</c>, <c>/usr/lib/libc.dylib</c>) instead of probing for a file, under
    /// CoreCLR and native AOT alike.
    /// </summary>
#if NET
    [LibraryImport("libc", EntryPoint = "kill")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Kill(int processId, int signal);
#else
    [DllImport("libc", EntryPoint = "kill")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Kill(int processId, int signal);
#endif
}
