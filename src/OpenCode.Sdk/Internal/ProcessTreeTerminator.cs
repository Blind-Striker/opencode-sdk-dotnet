using System.ComponentModel;
using System.Diagnostics;
#if !NET
using System.Globalization;
#endif

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Ends a launched server's whole process tree. Modern targets use the runtime's
/// entire-process-tree kill; net472/netstandard2.0 on Windows shell out to
/// <c>taskkill /pid … /T /F</c> — the reference client's own Windows group kill
/// (cross-spawn-spawner.ts:299); downlevel non-Windows kills the root, whose children the
/// stdin-EOF lease has already released. Trap: Polyfill also defines
/// <c>Kill(entireProcessTree)</c> for downlevel targets, but it maps to the plain
/// <c>Kill()</c> — the <c>#if</c> below is what keeps the tree kill real.
/// </summary>
internal static class ProcessTreeTerminator
{
    public static void Kill(Process process)
    {
        try
        {
#if NET
            process.Kill(entireProcessTree: true);
#else
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // The absolute path to the system taskkill.exe, rather than a bare "taskkill"
                // resolved through PATH: same binary the reference client invokes
                // (cross-spawn-spawner.ts:299), pinned to its well-known system location.
                var taskkillPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe");
                using var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = taskkillPath,
                    Arguments = "/pid " + process.Id.ToString(CultureInfo.InvariantCulture) + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                _ = killer?.WaitForExit(10_000);
                return;
            }

            process.Kill();
#endif
        }
        catch (InvalidOperationException)
        {
            // Already exited; nothing left to end.
        }
        catch (Win32Exception)
        {
            // Exited between the check and the kill, or the tree is already dying.
        }
    }
}
