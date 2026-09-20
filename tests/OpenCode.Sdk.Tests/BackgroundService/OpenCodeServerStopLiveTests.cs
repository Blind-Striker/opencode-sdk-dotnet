using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// Exact-pin process evidence for the stop door (design §13.3): the accepted pin's own
/// <c>serve --service</c> daemon under isolated roots, one per test because the test ends it —
/// stopped by registration file from this process and by channel from an isolated one, its
/// registration and handoff sidecar removed once it is gone; and a registration that names
/// another pid, which stops nothing and leaves the daemon answering.
/// </summary>
[ClassDataSource<PinnedManagedServiceFixture>]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class OpenCodeServerStopLiveTests(PinnedManagedServiceFixture service)
{
    private const string SidecarSuffix = ".pty-handoff";
    private const string SidecarContent = "{\"source\":{},\"handoff\":null,\"expiresAt\":0}";
    private const string OtherPassword = "not-the-daemon-s-p455";
    private static readonly RealFileSystem FileSystem = new();
    private static readonly TimeSpan ExitBound = TimeSpan.FromSeconds(30);

    [Test]
    [Timeout(180_000)]
    public async Task StopAsync_Should_End_The_Registered_Daemon_And_Remove_Its_Files(CancellationToken cancellationToken)
    {
        var sidecar = service.RegistrationFile + SidecarSuffix;
        Seed(sidecar, SidecarContent);

        await OpenCodeServer.StopAsync(new OpenCodeServerStopOptions { RegistrationFilePath = service.RegistrationFile }, cancellationToken);

        await Assert.That(await ProcessObservation.ObserveExitWithinAsync(service.ProcessId, ExitBound, cancellationToken)).IsTrue();
        await Assert.That(FileSystem.File.Exists(service.RegistrationFile)).IsFalse();
        await Assert.That(FileSystem.File.Exists(sidecar)).IsFalse();
    }

    [Test]
    [Timeout(180_000)]
    public async Task StopAsync_Should_Stop_The_Registered_Daemon_By_Channel_From_An_Isolated_Process(CancellationToken cancellationToken)
    {
        var environment = service.Environment.ToDictionary(
            static pair => pair.Key, static pair => (string?)pair.Value, StringComparer.Ordinal);

        var result = await new ServiceFixtureCommand(FileSystem)
            .RunAsync(["stop-channel", PinnedManagedServiceFixture.Channel], environment, cancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardOutput.Trim()).IsEqualTo("stopped").Because(result.StandardError);
        await Assert.That(await ProcessObservation.ObserveExitWithinAsync(service.ProcessId, ExitBound, cancellationToken)).IsTrue();
        await Assert.That(FileSystem.File.Exists(service.RegistrationFile)).IsFalse();
    }

    [Test]
    [Timeout(180_000)]
    public async Task StopAsync_Should_Leave_The_Daemon_Alive_When_The_Registration_Names_Another_Pid(CancellationToken cancellationToken)
    {
        // A registration carrying the daemon's endpoint but a pid no process runs under: the info
        // answer's pid does not match, so nothing is asked of the daemon, and no process is
        // signalled; the dead record goes, the daemon's own stays.
        var other = FileSystem.Path.Combine(service.RunRoot, "other-registration.json");
        Seed(other, ServiceRegistrationDocument.Compose("other", service.Version, service.Endpoint, int.MaxValue, OtherPassword));

        await OpenCodeServer.StopAsync(new OpenCodeServerStopOptions { RegistrationFilePath = other }, cancellationToken);

        await Assert.That(FileSystem.File.Exists(other)).IsFalse();
        await Assert.That(FileSystem.File.Exists(service.RegistrationFile)).IsTrue();
        await Assert.That(ProcessObservation.IsRunning(service.ProcessId)).IsTrue();
        var still = await OpenCodeServer.DiscoverAsync(
            new OpenCodeServerDiscoverOptions { RegistrationFilePath = service.RegistrationFile }, cancellationToken);
        await using var _ = still;
        await Assert.That(still).IsNotNull();
        await Assert.That(still!.ProcessId).IsEqualTo(service.ProcessId);
    }

    /// <summary>Writes one file beside the daemon's own; synchronous, the shape every test leg's Testably asset has.</summary>
    private static void Seed(string path, string content) => FileSystem.File.WriteAllText(path, content);
}
