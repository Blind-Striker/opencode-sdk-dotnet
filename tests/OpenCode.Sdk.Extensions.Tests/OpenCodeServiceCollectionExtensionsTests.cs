using Microsoft.Extensions.DependencyInjection;
using OpenCode.Sdk.Extensions.Tests.Support;

namespace OpenCode.Sdk.Extensions.Tests;

public sealed class OpenCodeServiceCollectionExtensionsTests
{
    private static readonly Uri Endpoint = new("http://localhost:4096");

    [Test]
    public async Task AddOpenCode_Should_Register_The_Root_Client_As_A_Singleton()
    {
        var services = new ServiceCollection().AddOpenCode(Endpoint);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<OpenCodeClient>();

        await Assert.That(provider.GetRequiredService<OpenCodeClient>()).IsSameReferenceAs(client);
    }

    [Test]
    public async Task AddOpenCode_Should_Resolve_The_Sessions_Client_From_The_Root_Client()
    {
        var services = new ServiceCollection().AddOpenCode(Endpoint);
        using var provider = services.BuildServiceProvider();

        var sessions = provider.GetRequiredService<SessionsClient>();

        await Assert.That(sessions).IsSameReferenceAs(provider.GetRequiredService<OpenCodeClient>().Sessions);
    }

    [Test]
    public async Task AddOpenCode_Should_Apply_The_Configure_Action_When_Building_The_Client()
    {
        var configured = new List<OpenCodeClientOptions>();
        var services = new ServiceCollection().AddOpenCode(Endpoint, configured.Add);
        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<OpenCodeClient>();

        await Assert.That(configured).HasSingleItem();
    }

    [Test]
    public async Task AddOpenCode_Should_Resolve_The_Sessions_Client_When_A_Configure_Action_Is_Supplied()
    {
        var services = new ServiceCollection().AddOpenCode(Endpoint, static options => options.Password = "secret");
        using var provider = services.BuildServiceProvider();

        var sessions = provider.GetRequiredService<SessionsClient>();

        await Assert.That(sessions).IsSameReferenceAs(provider.GetRequiredService<OpenCodeClient>().Sessions);
    }

    [Test]
    public async Task AddOpenCode_Should_Send_Through_The_Caller_Owned_Http_Client()
    {
        var payload = new FixtureLoader().LoadJson("known-health.json");
        using var handler = RecordingHttpHandler.RespondingJson(payload);
        using var httpClient = new HttpClient(handler);
        var services = new ServiceCollection().AddOpenCode(httpClient, options => options.Endpoint = Endpoint);
        using var provider = services.BuildServiceProvider();

        var response = await provider.GetRequiredService<OpenCodeClient>().GetHealthAsync();

        await Assert.That(response.Health.Healthy).IsTrue();
        await Assert.That(handler.RequestUris.Single()).IsEqualTo(new Uri("http://localhost:4096/api/health"));
    }

    [Test]
    public async Task AddOpenCode_Should_Resolve_The_Sessions_Client_When_The_Http_Client_Is_Caller_Owned()
    {
        using var handler = RecordingHttpHandler.RespondingJson("{}");
        using var httpClient = new HttpClient(handler);
        var services = new ServiceCollection().AddOpenCode(httpClient, options => options.Endpoint = Endpoint);
        using var provider = services.BuildServiceProvider();

        var sessions = provider.GetRequiredService<SessionsClient>();

        await Assert.That(sessions).IsSameReferenceAs(provider.GetRequiredService<OpenCodeClient>().Sessions);
    }

    [Test]
    public async Task AddOpenCode_Should_Refuse_A_Null_Service_Collection()
    {
        _ = await Assert.That(() => ((IServiceCollection)null!).AddOpenCode(Endpoint))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddOpenCode_Should_Refuse_A_Null_Endpoint()
    {
        _ = await Assert.That(() => new ServiceCollection().AddOpenCode(endpoint: null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddOpenCode_Should_Refuse_A_Null_Http_Client()
    {
        _ = await Assert.That(() => new ServiceCollection().AddOpenCode(httpClient: null!, static _ => { }))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddOpenCode_Should_Refuse_A_Null_Configure_Action_When_The_Http_Client_Is_Caller_Owned()
    {
        using var handler = RecordingHttpHandler.RespondingJson("{}");
        using var httpClient = new HttpClient(handler);

        _ = await Assert.That(() => new ServiceCollection().AddOpenCode(httpClient, configure: null!))
            .Throws<ArgumentNullException>();
    }
}
