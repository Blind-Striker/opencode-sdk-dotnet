using OpenCode.Sdk.TestSupport;
using Testably.Abstractions.Testing;

namespace OpenCode.Sdk.Tests;

public sealed class ServerIsolationTests
{
    [Test]
    public async Task Environment_Should_Name_An_Existing_Home_Beneath_The_Run_Root()
    {
        var fileSystem = new MockFileSystem();
        var runRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "run");
        _ = fileSystem.Directory.CreateDirectory(runRoot);

        var environment = ServerIsolation.Environment(fileSystem, runRoot);

        // The two places an owned server takes its home from: upstream's own override
        // (packages/util/src/global.ts:17 at the pin) and the variable os.homedir() reads on this
        // platform. MockFileSystem simulates the host OS, so the platform branch matches it.
        _ = environment.TryGetValue("OPENCODE_TEST_HOME", out var home);
        await Assert.That(fileSystem.Directory.Exists(home)).IsTrue();
        await Assert.That(home is { } path
            && path.StartsWith(runRoot + fileSystem.Path.DirectorySeparatorChar, StringComparison.Ordinal)).IsTrue();
        _ = environment.TryGetValue(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME", out var platformHome);
        await Assert.That(platformHome).IsEqualTo(home);
    }

    [Test]
    public async Task Environment_Should_Leave_An_Interactive_Shell_Nothing_To_Ask()
    {
        var fileSystem = new MockFileSystem();
        var runRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "run");

        _ = ServerIsolation.Environment(fileSystem, runRoot).TryGetValue("HOME", out var home);

        // A terminal the server opens runs the user's own shell in this home. zsh answers a home
        // without startup files with its first-run wizard, which waits for a key and swallows the
        // line a test submits.
        await Assert.That(home is { } path && fileSystem.File.Exists(fileSystem.Path.Combine(path, ".zshrc"))).IsTrue();
    }
}
