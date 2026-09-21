using System.Text;
using OpenCode.Sdk.Internal.BackgroundService;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// Exact-pin process evidence for the Ensure door (design §13.2): the accepted pin's own
/// <c>serve --service</c> daemon, started by Ensure itself under isolated roots — reusing the
/// default <c>opencode serve --service</c> command resolved through the forwarding shim (test a),
/// elected by ten concurrent callers (test b), recovered from an unresponsive registered daemon
/// after three timeouts (test c), and replacing a version-mismatched one (test d). Each test owns
/// its own <see cref="EnsureServiceContext"/>; none touches the maintainer's real daemon.
/// </summary>
/// <remarks>
/// Keyless <c>[NotInParallel]</c> rather than the server-process key, the rule research log Q157
/// sets for a test whose assertion depends on a wall-clock bound the host can miss under load: the
/// three-timeout recovery and the replace proof both ride the pinned probe bound and a process exit
/// the host can delay, the same profile the stop liveness proof records (Q172). Running alone after
/// every other test keeps the host quiet while those bounds hold.
/// </remarks>
[NotInParallel]
public sealed class OpenCodeServerEnsureLiveTests
{
    private static readonly RealFileSystem FileSystem = new();
    private static readonly TimeSpan ExitBound = TimeSpan.FromSeconds(30);

    /// <summary>The election timing test (c) injects: fast probes, and a spawn delay long enough that
    /// the three-timeout recovery fires before the spawn-delay gate could start a contender early.</summary>
    private static readonly ServiceTiming Accelerated = ServiceTiming.Default with
    {
        RequestTimeout = TimeSpan.FromMilliseconds(200),
        PollInterval = TimeSpan.FromMilliseconds(10),
        SpawnDelay = TimeSpan.FromMilliseconds(500),
    };

    [Test]
    [Timeout(180_000)]
    public async Task EnsureAsync_Should_Start_The_Source_Run_Daemon_By_Default_Command_And_Channel(CancellationToken cancellationToken)
    {
        await using var context = await EnsureServiceContext.CreateAsync(cancellationToken);

        // The default command resolves through the isolated process's PATH, whose first entry is
        // the forwarding shim: the shipped PATH/PATHEXT resolution, not an explicit path.
        var result = await new ServiceFixtureCommand(FileSystem)
            .RunAsync(["ensure-channel", "local"], context.IsolatedProcessEnvironment(), cancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);

        var registration = await ReadRegistrationAsync(context.RegistrationFile);
        await Assert.That(result.StandardOutput.Trim())
            .IsEqualTo(ServiceFixtureOutput.EnsuredLine(registration.ProcessId, registration.Endpoint))
            .Because(result.StandardError);

        // A source run compiles its identity as the literal `local` (packages/cli/src/version.ts).
        await Assert.That(registration.Version).IsEqualTo("local");
        await Assert.That(ProcessObservation.IsRunning(registration.ProcessId)).IsTrue();

        context.TrackProcess(registration.ProcessId);
    }

    [Test]
    [Timeout(180_000)]
    public async Task EnsureAsync_Should_Elect_One_Service_Across_Ten_Concurrent_Callers(CancellationToken cancellationToken)
    {
        await using var context = await EnsureServiceContext.CreateAsync(cancellationToken);

        var options = EnsureOptions(context);
        var servers = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => OpenCodeServer.EnsureAsync(options, cancellationToken)));

        try
        {
            var elected = servers[0];
            await Assert.That(ProcessObservation.IsRunning(elected.ProcessId)).IsTrue();

            // Every caller's handle names the same registration: one service was elected.
            foreach (var server in servers)
            {
                await Assert.That(server.ProcessId).IsEqualTo(elected.ProcessId);
                await Assert.That(server.Endpoint).IsEqualTo(elected.Endpoint);
            }

            context.TrackProcess(elected.ProcessId);
        }
        finally
        {
            foreach (var server in servers)
            {
                await server.DisposeAsync();
            }
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task EnsureAsync_Should_Recover_From_An_Unresponsive_Daemon_After_Three_Timeouts(CancellationToken cancellationToken)
    {
        await using var context = await EnsureServiceContext.CreateAsync(cancellationToken);
        await using var stall = await ServiceDaemonStandIn.StartAsync(FileSystem, "stall", cancellationToken);

        await SeedAsync(context.RegistrationFile, ServiceRegistrationDocument.Compose(
            "stalled", "local", stall.Endpoint, stall.ProcessId, "stall-p455"));

        var server = await OpenCodeServer.EnsureWithTimingAsync(EnsureOptions(context), Accelerated, cancellationToken);
        try
        {
            await Assert.That(await stall.ObserveExitWithinAsync(ExitBound, cancellationToken)).IsTrue()
                .Because("the unresponsive daemon should be terminated after three timeouts");
            await Assert.That(server.ProcessId).IsNotEqualTo(stall.ProcessId);
            await Assert.That(ProcessObservation.IsRunning(server.ProcessId)).IsTrue();

            context.TrackProcess(server.ProcessId);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task EnsureAsync_Should_Replace_A_Version_Mismatched_Service(CancellationToken cancellationToken)
    {
        await using var context = await EnsureServiceContext.CreateAsync(cancellationToken);
        await using var stale = await ServiceDaemonStandIn.StartAsync(FileSystem, "stale", cancellationToken);

        // A registration naming the stale daemon's own identity: the probe reads it ready at the
        // never-built 0.0.0-stale version, which the Replace policy refuses.
        await SeedAsync(context.RegistrationFile, ServiceRegistrationDocument.Compose(
            "stale", "0.0.0-stale", stale.Endpoint, stale.ProcessId, "stale-p455"));

        var options = EnsureOptions(context);
        options.ExpectedVersion = "local";
        options.VersionPolicy = OpenCodeServerVersionPolicy.Replace;

        var server = await OpenCodeServer.EnsureAsync(options, cancellationToken);
        try
        {
            await Assert.That(await stale.ObserveExitWithinAsync(ExitBound, cancellationToken)).IsTrue()
                .Because("the version-mismatched service should be terminated by the replace");
            await Assert.That(server.ProcessId).IsNotEqualTo(stale.ProcessId);
            await Assert.That(ProcessObservation.IsRunning(server.ProcessId)).IsTrue();

            // The replacement is the source-run daemon at its own version, now the registered one.
            var registration = await ReadRegistrationAsync(context.RegistrationFile);
            await Assert.That(registration.Version).IsEqualTo("local");
            await Assert.That(registration.ProcessId).IsEqualTo(server.ProcessId);

            context.TrackProcess(server.ProcessId);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    /// <summary>The Ensure options every direct caller uses: the isolated registration file by path
    /// (the test process's own roots are the developer's, never the isolated ones), the forwarding
    /// shim as the explicit command, and the isolated roots as the spawned service's environment.</summary>
    private static OpenCodeServerEnsureOptions EnsureOptions(EnsureServiceContext context) =>
        new()
        {
            RegistrationFilePath = context.RegistrationFile,
            Command = [context.ShimPath, "serve", "--service"],
            Environment = context.Environment,
        };

    private static async Task<ServiceRegistration> ReadRegistrationAsync(string path)
    {
        using var stream = FileSystem.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, CancellationToken.None).ConfigureAwait(false);
        return ServiceRegistrationReader.TryRead(buffer.ToArray())
            ?? throw new InvalidOperationException($"No usable registration at '{path}'.");
    }

    private static async Task SeedAsync(string path, string document)
    {
        _ = FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(path)!);
        using var stream = FileSystem.FileStream.New(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var bytes = Encoding.UTF8.GetBytes(document);
        await stream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
    }
}
