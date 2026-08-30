using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests;

[NotInParallel(ParallelConstraintKeys.ServerProcess)]
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

    [Test]
    [Timeout(240_000)]
    public async Task DisposeAsync_Should_Be_Idempotent_When_Called_Twice(CancellationToken cancellationToken)
    {
        var runRoot = new TestRunRoot(FileSystem);
        using var _ = runRoot;
        var pinnedCommand = new PinnedServerCommand(FileSystem);

        var adapter = await CliWrapServerAdapter.StartAsync(
            pinnedCommand.Resolve(),
            ServerIsolation.Environment(FileSystem, runRoot.Path),
            FileSystem.Path.Combine(pinnedCommand.RepositoryRoot, "external", "opencode", "packages", "cli"),
            readinessTimeout: TimeSpan.FromMinutes(3),
            cancellationToken: cancellationToken);

        await adapter.DisposeAsync();

        // The guard's short-circuit makes a second call a no-op unconditionally, regardless of
        // whether the first call's forced-kill wait actually raced _execution's completion - the
        // exact interleaving the review flagged. That race needs no dedicated timing
        // reproduction: the guard returns before touching _forceKill either way, so this
        // deterministic double-dispose call is the whole pin.
        await adapter.DisposeAsync();
    }
}
