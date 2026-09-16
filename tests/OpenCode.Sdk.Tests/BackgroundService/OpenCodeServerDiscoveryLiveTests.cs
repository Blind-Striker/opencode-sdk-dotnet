using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// Exact-pin process evidence for background-service discovery (design §13.1): the accepted
/// pin's own <c>serve --service</c> daemon under isolated roots, found by channel from an isolated
/// process and by registration file in this one; a client that answers health; the
/// <c>ExpectedVersion</c> gate; a handle that owns nothing and leaves the shared service alive on
/// disposal. The environment-reading proofs against a loopback daemon live in
/// <see cref="OpenCodeServerDiscoveryIsolatedProcessTests"/>.
/// </summary>
[ClassDataSource<PinnedManagedServiceFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class OpenCodeServerDiscoveryLiveTests(PinnedManagedServiceFixture service)
{
    private static readonly RealFileSystem FileSystem = new();

    [Test]
    [Timeout(120_000)]
    public async Task DiscoverAsync_Should_Find_The_Accepted_Pin_Managed_Service_By_Channel(CancellationToken cancellationToken)
    {
        // The same run-root environment the service received, from a process that has no other
        // way to find it: what a consumer on this machine would see, minus the developer profile.
        var environment = service.Environment.ToDictionary(
            static pair => pair.Key, static pair => (string?)pair.Value, StringComparer.Ordinal);

        var result = await new ServiceFixtureCommand(FileSystem)
            .RunAsync(["discover-channel", PinnedManagedServiceFixture.Channel], environment, cancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardOutput.Trim())
            .IsEqualTo(ServiceFixtureOutput.FoundLine(service.ProcessId, service.Endpoint))
            .Because(result.StandardError);
    }

    [Test]
    [Timeout(120_000)]
    public async Task DiscoverAsync_Should_Find_The_Managed_Service_By_Registration_File(CancellationToken cancellationToken)
    {
        var server = await DiscoverByFileAsync(cancellationToken);
        await using var _ = server;

        await Assert.That(server.OwnsProcess).IsFalse();
        await Assert.That(server.ProcessId).IsEqualTo(service.ProcessId);
        await Assert.That(server.Endpoint).IsEqualTo(service.Endpoint);
        await Assert.That(server.Username).IsEqualTo("opencode");
    }

    [Test]
    [Timeout(120_000)]
    public async Task Discovered_Server_Should_Create_A_Client_That_Answers_Health(CancellationToken cancellationToken)
    {
        var server = await DiscoverByFileAsync(cancellationToken);
        await using var _ = server;

        using var client = server.CreateClient();
        var health = await client.GetHealthAsync(cancellationToken: cancellationToken);

        await Assert.That(health.Health.Healthy).IsTrue();
        await Assert.That(health.Health.Pid).IsEqualTo(server.ProcessId);
        await Assert.That(health.Health.Version).IsEqualTo(service.Version);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Disposing_Discovered_Server_Should_Leave_The_Shared_Service_Alive(CancellationToken cancellationToken)
    {
        var server = await DiscoverByFileAsync(cancellationToken);

        await server.DisposeAsync();
        // A second disposal is a no-op by contract; this is the idempotence proof.
        await server.DisposeAsync();

        await Assert.That(ProcessObservation.IsRunning(service.ProcessId)).IsTrue();
        var again = await DiscoverByFileAsync(cancellationToken);
        await using var _ = again;
        using var client = again.CreateClient();
        var health = await client.GetHealthAsync(cancellationToken: cancellationToken);
        await Assert.That(health.Health.Pid).IsEqualTo(service.ProcessId);
    }

    [Test]
    [Timeout(120_000)]
    public async Task DiscoverAsync_Should_Apply_ExpectedVersion_Against_The_Accepted_Server(CancellationToken cancellationToken)
    {
        var matching = await OpenCodeServer.DiscoverAsync(
            new OpenCodeServerDiscoverOptions { RegistrationFilePath = service.RegistrationFile, ExpectedVersion = service.Version },
            cancellationToken);
        await using var _ = matching;
        var mismatching = await OpenCodeServer.DiscoverAsync(
            new OpenCodeServerDiscoverOptions { RegistrationFilePath = service.RegistrationFile, ExpectedVersion = "0.0.0-never-1" },
            cancellationToken);

        await Assert.That(matching).IsNotNull();
        await Assert.That(mismatching).IsNull();
    }

    private async Task<OpenCodeServer> DiscoverByFileAsync(CancellationToken cancellationToken)
    {
        var server = await OpenCodeServer.DiscoverAsync(
            new OpenCodeServerDiscoverOptions { RegistrationFilePath = service.RegistrationFile },
            cancellationToken);
        return server ?? throw new InvalidOperationException(
            $"The managed service registered at '{service.RegistrationFile}' was not discovered; the fixture reported it ready.");
    }
}
