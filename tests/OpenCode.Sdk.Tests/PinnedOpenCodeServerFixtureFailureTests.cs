using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// Pins the failure-path log-retention contract: a real <see cref="PinnedOpenCodeServerFixture"/>
/// startup failure must retain stdout.log/stderr.log under the run root, not just the lower-level
/// adapter's own in-memory buffers. Drives the fixture itself (its internal command-override
/// seam), not the shared per-session instance the other fixture tests share.
/// </summary>
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class PinnedOpenCodeServerFixtureFailureTests
{
    private static readonly RealFileSystem FileSystem = new();

    [Test]
    public async Task InitializeAsync_Failure_Should_Retain_Logs_Under_The_Run_Root()
    {
        var pinnedCommand = new PinnedServerCommand(FileSystem);
        var fixture = new PinnedOpenCodeServerFixture(
            ["bun", "-e", "console.error('boom'); process.exit(7)"],
            pinnedCommand.RepositoryRoot);

        _ = await Assert.That(fixture.InitializeAsync).Throws<InvalidOperationException>();

        var runRootPath = fixture.RunRoot.Path;
        await fixture.DisposeAsync();

        var stdoutPath = FileSystem.Path.Combine(runRootPath, "logs", "stdout.log");
        var stderrPath = FileSystem.Path.Combine(runRootPath, "logs", "stderr.log");
        await Assert.That(FileSystem.File.Exists(stdoutPath)).IsTrue();
        await Assert.That(FileSystem.File.Exists(stderrPath)).IsTrue();
    }
}
