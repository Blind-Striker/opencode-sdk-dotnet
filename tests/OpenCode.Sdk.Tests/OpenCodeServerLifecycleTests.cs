using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests;

[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class OpenCodeServerLifecycleTests
{
    private static readonly RealFileSystem FileSystem = new();

    private static async Task<(OpenCodeServer Server, TestRunRoot Root)> StartPinnedAsync(
        TimeSpan? gracefulShutdownTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var runRoot = new TestRunRoot(FileSystem);
        var pinnedCommand = new PinnedServerCommand(FileSystem);
        var options = new OpenCodeServerOptions
        {
            Command = pinnedCommand.Resolve(),
            // Bun's own workspace/tsconfig discovery for the pinned monorepo's JSX packages
            // (the TUI's solid-js tree) walks from the process's working directory, not from
            // the absolute entry-file path. A working directory outside the checkout (the
            // original design of an isolated per-run scratch "cwd") leaves that discovery unable
            // to find the workspace root, and the source-run server fails before readiness with
            // "Cannot find module 'react/jsx-dev-runtime'" — confirmed by direct repro. Anchoring
            // the working directory at the CLI package (this repo's own historical smoke-test
            // convention) is what upstream's own "dev" script does; state/data/cache/config stay
            // isolated through the environment below regardless of this directory.
            WorkingDirectory = FileSystem.Path.Combine(
                pinnedCommand.RepositoryRoot, "external", "opencode", "packages", "cli"),
            Environment = ServerIsolation.Environment(FileSystem, runRoot.Path),
            ReadinessTimeout = TimeSpan.FromMinutes(3),
        };
        if (gracefulShutdownTimeout is { } grace)
        {
            options.GracefulShutdownTimeout = grace;
        }

        return (await OpenCodeServer.StartAsync(options, cancellationToken), runRoot);
    }

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
    public async Task StartAsync_Should_Report_Readiness_And_Answer_Health(CancellationToken cancellationToken)
    {
        var (server, runRoot) = await StartPinnedAsync(cancellationToken: cancellationToken);
        using var _ = runRoot;
        await using var __ = server;

        await Assert.That(server.Endpoint.IsLoopback).IsTrue();
        await Assert.That(server.Endpoint.Port).IsNotEqualTo(0);

        using var client = server.CreateClient();
        var health = await client.GetHealthAsync(cancellationToken: cancellationToken);

        await Assert.That(health.Health.Healthy).IsTrue();
        // Process truth: the server answering health is the exact child this start owns. bun
        // runs the entry in-process, so the reported pid is the spawned pid. If a platform leg
        // ever disproves this, record the deviation — do not soften the assertion silently.
        await Assert.That(health.Health.Pid).IsEqualTo(server.ProcessId);
    }

    [Test]
    [Timeout(240_000)]
    public async Task DisposeAsync_Should_End_The_Server_Through_The_Stdin_Lease(CancellationToken cancellationToken)
    {
        var (server, runRoot) = await StartPinnedAsync(cancellationToken: cancellationToken);
        using var _ = runRoot;
        var processId = server.ProcessId;

        await server.DisposeAsync();

        await Assert.That(IsProcessRunning(processId)).IsFalse();
    }

    [Test]
    [Timeout(240_000)]
    public async Task DisposeAsync_Should_End_The_Server_Through_The_Forced_Kill_Escalation(CancellationToken cancellationToken)
    {
        // Zero grace: the wait is already expired when disposal reaches it, so the forced
        // tree-kill path executes deterministically (net472 exercises the taskkill arm).
        var (server, runRoot) = await StartPinnedAsync(
            gracefulShutdownTimeout: TimeSpan.Zero, cancellationToken: cancellationToken);
        using var _ = runRoot;
        var processId = server.ProcessId;

        await server.DisposeAsync();

        await Assert.That(IsProcessRunning(processId)).IsFalse();
    }

    [Test]
    [Timeout(120_000)]
    public async Task StartAsync_Should_Refuse_A_Server_That_Exits_Before_Readiness(CancellationToken cancellationToken)
    {
        var failure = await Assert.That(async () => await OpenCodeServer.StartAsync(
            new OpenCodeServerOptions
            {
                Command = ["bun", "-e", "console.error('boom'); process.exit(7)"],
            },
            cancellationToken)).Throws<OpenCodeServerException>();

        await Assert.That(failure!.Message).Contains("exited with code 7");
        await Assert.That(failure.Message).Contains("boom");
    }

    [Test]
    [Timeout(120_000)]
    public async Task StartAsync_Should_Refuse_A_Server_That_Never_Reports_Readiness(CancellationToken cancellationToken)
    {
        var failure = await Assert.That(async () => await OpenCodeServer.StartAsync(
            new OpenCodeServerOptions
            {
                Command = ["bun", "-e", "setTimeout(() => {}, 120000)"],
                ReadinessTimeout = TimeSpan.FromSeconds(2),
            },
            cancellationToken)).Throws<OpenCodeServerException>();

        await Assert.That(failure!.Message).Contains("did not report readiness");
    }

    [Test]
    [Timeout(120_000)]
    public async Task StartAsync_Should_Refuse_A_Non_Contract_First_Line(CancellationToken cancellationToken)
    {
        var failure = await Assert.That(async () => await OpenCodeServer.StartAsync(
            new OpenCodeServerOptions
            {
                Command = ["bun", "-e", "console.log('hello'); setTimeout(() => {}, 120000)"],
            },
            cancellationToken)).Throws<OpenCodeServerException>();

        await Assert.That(failure!.Message).Contains("readiness contract");
    }

    [Test]
    [Timeout(120_000)]
    public async Task StartAsync_Should_Refuse_A_Missing_Executable(CancellationToken cancellationToken)
    {
        var failure = await Assert.That(async () => await OpenCodeServer.StartAsync(
            new OpenCodeServerOptions
            {
                Command = ["opencode-sdk-test-missing-executable"],
            },
            cancellationToken)).Throws<OpenCodeServerException>();

        await Assert.That(failure!.Message).Contains("Failed to start");
    }

    [Test]
    [Timeout(120_000)]
    public async Task StartAsync_Should_Surface_Caller_Cancellation(CancellationToken cancellationToken)
    {
        // Linked to the test's own injected token so the manufactured 200ms caller-cancellation
        // still composes with whatever the [Timeout] attribute (or an external test-run
        // cancellation) also asks for, rather than replacing it outright.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));

        _ = await Assert.That(async () => await OpenCodeServer.StartAsync(
            new OpenCodeServerOptions
            {
                Command = ["bun", "-e", "setTimeout(() => {}, 120000)"],
            },
            cancellation.Token)).Throws<OperationCanceledException>();
    }
}
