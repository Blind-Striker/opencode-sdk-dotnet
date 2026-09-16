using System.Globalization;
using System.Net;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// Exact-pin process evidence for background-service discovery (design §13.1): the accepted
/// pin's own <c>serve --service</c> daemon under isolated roots, found by channel from an isolated
/// process and by registration file in this one; a client that answers health; the
/// <c>ExpectedVersion</c> gate; a handle that owns nothing and leaves the shared service alive on
/// disposal. The two isolated-process cases prove the default shared registration and the home
/// fallback against a loopback health server, so no test ever reads the developer's own profile.
/// </summary>
[ClassDataSource<PinnedManagedServiceFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class OpenCodeServerDiscoveryLiveTests(PinnedManagedServiceFixture service)
{
    private const string IsolatedPassword = "isolated-p455";
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
        await Assert.That(result.StandardOutput.Trim()).IsEqualTo(FoundLine(service.ProcessId, service.Endpoint));
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

    [Test]
    [Timeout(120_000)]
    public async Task DiscoverAsync_Should_Use_The_Default_Shared_Registration_In_An_Isolated_Process(CancellationToken cancellationToken)
    {
        using var root = new TestRunRoot(FileSystem);
        await using var health = LoopbackHttpServer.Start(static _ => ReadyHealth());
        var state = FileSystem.Path.Combine(root.Path, "state");
        Seed(FileSystem.Path.Combine(state, "opencode", "service.json"), health.Endpoint);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_STATE_HOME"] = state,
            ["XDG_CONFIG_HOME"] = FileSystem.Path.Combine(root.Path, "config"),
            ["OPENCODE_CONFIG_DIR"] = null,
        };

        var result = await new ServiceFixtureCommand(FileSystem).RunAsync(["discover-default"], environment, cancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardOutput.Trim()).IsEqualTo(FoundLine(ServiceHealthBodyData.Pid, health.Endpoint));
        await Assert.That(health.RequestPaths).IsEquivalentTo(["/api/health"]);
    }

    [Test]
    [Timeout(120_000)]
    public async Task DiscoverAsync_Should_Fall_Back_To_The_Home_Directory_In_An_Isolated_Process(CancellationToken cancellationToken)
    {
        using var root = new TestRunRoot(FileSystem);
        await using var health = LoopbackHttpServer.Start(static _ => ReadyHealth());
        var home = FileSystem.Path.Combine(root.Path, "home");
        Seed(FileSystem.Path.Combine(home, ".local", "state", "opencode", "service.json"), health.Endpoint);
        // No XDG state root at all: the only way to the registration is the redirected home,
        // read through USERPROFILE on Windows and HOME elsewhere (the libuv rule the SDK follows).
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_STATE_HOME"] = null,
            ["XDG_CONFIG_HOME"] = null,
            ["OPENCODE_CONFIG_DIR"] = null,
            ["HOME"] = home,
            ["USERPROFILE"] = home,
        };

        var result = await new ServiceFixtureCommand(FileSystem).RunAsync(["discover-default"], environment, cancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardOutput.Trim()).IsEqualTo(FoundLine(ServiceHealthBodyData.Pid, health.Endpoint));
        await Assert.That(health.RequestPaths).IsEquivalentTo(["/api/health"]);
    }

    private async Task<OpenCodeServer> DiscoverByFileAsync(CancellationToken cancellationToken)
    {
        var server = await OpenCodeServer.DiscoverAsync(
            new OpenCodeServerDiscoverOptions { RegistrationFilePath = service.RegistrationFile },
            cancellationToken);
        return server ?? throw new InvalidOperationException(
            $"The managed service registered at '{service.RegistrationFile}' was not discovered; the fixture reported it ready.");
    }

    private static string FoundLine(int processId, Uri endpoint) =>
        $"found owns=false pid={processId.ToString(CultureInfo.InvariantCulture)} endpoint={endpoint}";

    private static LoopbackHttpResponse ReadyHealth() =>
        new() { StatusCode = HttpStatusCode.OK, Body = ServiceHealthBodyData.Ready, ContentType = "application/json" };

    /// <summary>Writes the registration the loopback daemon (pid 42, version 0.0.0-test) would have published.</summary>
    private static void Seed(string path, Uri endpoint)
    {
        var document = "{\"id\":\"isolated\",\"version\":\"" + ServiceHealthBodyData.Version + "\",\"url\":\"" + endpoint
            + "\",\"pid\":" + ServiceHealthBodyData.Pid.ToString(CultureInfo.InvariantCulture)
            + ",\"password\":\"" + IsolatedPassword + "\"}";
        _ = FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(path)!);
        FileSystem.File.WriteAllText(path, document);
    }
}
