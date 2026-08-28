using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests;

public sealed class CliWrapServerAdapterTests
{
    private static readonly RealFileSystem FileSystem = new();

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Test]
    [Timeout(240_000)]
    [NotInParallel("cliwrap-server-adapter")]
    public async Task DisposeAsync_Should_End_The_Server_Through_The_Forced_Kill_Escalation(CancellationToken cancellationToken)
    {
        var runRoot = new TestRunRoot(FileSystem);
        using var _ = runRoot;
        var pinnedCommand = new PinnedServerCommand(FileSystem);

        // Zero grace: the wait is already expired when disposal reaches it, so the forced
        // tree-kill path - and its own secondary bound on the final await - executes
        // deterministically (mirrors OpenCodeServerLifecycleTests' equivalent escalation test for
        // the production launcher).
        var adapter = await CliWrapServerAdapter.StartAsync(
            pinnedCommand.Resolve(),
            ServerIsolation.Environment(FileSystem, runRoot.Path),
            FileSystem.Path.Combine(pinnedCommand.RepositoryRoot, "external", "opencode", "packages", "cli"),
            readinessTimeout: TimeSpan.FromMinutes(3),
            gracefulShutdownTimeout: TimeSpan.Zero,
            cancellationToken: cancellationToken);
        var processId = adapter.ProcessId;

        await adapter.DisposeAsync();

        await Assert.That(IsProcessRunning(processId)).IsFalse();
    }
}
