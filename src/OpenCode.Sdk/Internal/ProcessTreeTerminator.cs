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
    /// <summary>Ends the process tree.</summary>
    /// <returns>
    /// True when the kill was issued; false when there was nothing left to end — the child had
    /// already exited, or its handle is no longer accessible to this process.
    /// </returns>
    public static bool TryKill(Process process)
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
                // taskkill's own exit is the confirmation that the tree kill was issued; a
                // taskkill that could not even be started never issued one.
                return killer?.WaitForExit(10_000) is true;
            }

            process.Kill();
#endif
            return true;
        }
        catch (InvalidOperationException)
        {
            // Already exited (or never started): there is no tree left to end, which is the same
            // end state the kill was asking for.
            return false;
        }
        catch (Win32Exception)
        {
            // Exited between the check and the kill, or the tree is already dying and its handle
            // is no longer accessible; either way this process has nothing further to issue.
            return false;
        }
    }
}
