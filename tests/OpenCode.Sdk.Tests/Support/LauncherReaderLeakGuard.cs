using System.Globalization;
using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// The whole-session release guarantee for the launcher's Windows output readers: every
/// <see cref="ChildOutputReader"/> thread a test started, directly or through a server it
/// launched, has ended by the time the session ends. Shared server fixtures are disposed after
/// the last test that uses them, before this hook runs, so a reader still alive here is a leak:
/// a server a test never disposed, or a launcher path that does not release its readers.
/// </summary>
public static class LauncherReaderLeakGuard
{
    [After(TestSession)]
    public static void No_Launcher_Reader_Should_Outlive_The_Session()
    {
        var live = ChildOutputReader.LiveReaders;
        if (live != 0)
        {
            throw new InvalidOperationException(
                live.ToString(CultureInfo.InvariantCulture)
                + " launcher output reader thread(s) are still alive at the end of the test session: a started server was not disposed, or a launcher path did not release its readers.");
        }
    }
}
