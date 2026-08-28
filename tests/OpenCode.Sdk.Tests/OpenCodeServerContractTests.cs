namespace OpenCode.Sdk.Tests;

public sealed class OpenCodeServerContractTests
{
    private static readonly Uri DoorEndpoint = new("http://127.0.0.1:4096");

    private const string DoorPassword = "lease-credential";

    private static OpenCodeServer CreateStartedDoor() => new(DoorEndpoint, DoorPassword);

    [Test]
    public async Task CreateClient_Should_Hand_The_Delegate_Identity_Unset_Options()
    {
        var server = CreateStartedDoor();
        OpenCodeClientOptions? observed = null;

        using var client = server.CreateClient(options => observed = options);

        await Assert.That(observed!.Endpoint).IsNull();
        await Assert.That(observed.Username).IsEqualTo("opencode");
        await Assert.That(observed.Password).IsNull();
    }

    [Test]
    public async Task CreateClient_Should_Refuse_A_Configured_Endpoint()
    {
        var server = CreateStartedDoor();

        var refusal = await Assert.That(() => server.CreateClient(options =>
            options.Endpoint = new Uri("http://example.test"))).Throws<InvalidOperationException>();

        await Assert.That(refusal!.Message).Contains("identity");
    }

    [Test]
    public async Task CreateClient_Should_Refuse_A_Configured_Password()
    {
        var server = CreateStartedDoor();

        _ = await Assert.That(() => server.CreateClient(options => options.Password = "other"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateClient_Should_Refuse_A_Changed_Username()
    {
        var server = CreateStartedDoor();

        _ = await Assert.That(() => server.CreateClient(options => options.Username = "admin"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateClient_Should_Allow_Behavior_Members()
    {
        var server = CreateStartedDoor();

        using var client = server.CreateClient(options =>
            options.Location = new LocationSelector { Directory = "/tmp/workspace" });

        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task CreateClient_Should_Build_A_New_Client_Per_Call()
    {
        var server = CreateStartedDoor();

        using var first = server.CreateClient();
        using var second = server.CreateClient();

        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }

    [Test]
    public async Task DisposeAsync_Should_Be_Idempotent_Without_A_Process()
    {
        var server = CreateStartedDoor();

        await server.DisposeAsync();
        await server.DisposeAsync();
    }

    [Test]
    public async Task StartAsync_Should_Refuse_An_Empty_Command()
    {
        _ = await Assert.That(async () => await OpenCodeServer.StartAsync(
            new OpenCodeServerOptions { Command = [] })).Throws<ArgumentException>();
    }

    [Test]
    public async Task StartAsync_Should_Refuse_A_Blank_Command_Entry()
    {
        _ = await Assert.That(async () => await OpenCodeServer.StartAsync(
            new OpenCodeServerOptions { Command = ["bun", " "] })).Throws<ArgumentException>();
    }

    [Test]
    public async Task StartAsync_Should_Refuse_A_Non_Positive_Readiness_Timeout()
    {
        _ = await Assert.That(async () => await OpenCodeServer.StartAsync(
            new OpenCodeServerOptions { ReadinessTimeout = TimeSpan.Zero })).Throws<ArgumentException>();
    }

    [Test]
    public async Task Endpoint_Should_Throw_The_Mock_Seam_Failure_On_A_Bare_Mock()
    {
        var mock = new MockableServer();

        var failure = await Assert.That(() => _ = mock.Endpoint).Throws<InvalidOperationException>();

        await Assert.That(failure!.Message).Contains("OpenCodeServer.Endpoint");
    }

    private sealed class MockableServer : OpenCodeServer
    {
    }
}
