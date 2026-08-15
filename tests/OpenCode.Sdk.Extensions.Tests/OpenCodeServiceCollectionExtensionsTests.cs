using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Sdk.Extensions.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Extensions.Tests;

public sealed class OpenCodeServiceCollectionExtensionsTests
{
    private static readonly Uri Endpoint = new("http://localhost:4096");

    [Test]
    public async Task AddOpenCode_Should_Resolve_The_Client_Through_The_Factory_Pipeline()
    {
        var payload = new FixtureLoader().LoadJson("known-health.json");
        using var handler = RecordingHttpHandler.RespondingJson(payload);
        var services = new ServiceCollection();
        _ = services.AddOpenCode(options => options.Endpoint = Endpoint)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var response = await provider.GetRequiredService<OpenCodeClient>().GetHealthAsync();

        await Assert.That(response.Health.Healthy).IsTrue();
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/health"));
    }

    [Test]
    public async Task AddOpenCode_Should_Register_The_Client_As_Transient_For_Handler_Rotation()
    {
        var services = new ServiceCollection();
        _ = services.AddOpenCode(options => options.Endpoint = Endpoint);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<OpenCodeClient>();
        var second = provider.GetRequiredService<OpenCodeClient>();

        await Assert.That(first).IsNotSameReferenceAs(second);
    }

    [Test]
    public async Task AddOpenCode_Should_Resolve_The_Sessions_Client_Directly()
    {
        var payload = new FixtureLoader().LoadJson("known-health.json");
        using var handler = RecordingHttpHandler.RespondingJson(payload);
        var services = new ServiceCollection();
        _ = services.AddOpenCode(options => options.Endpoint = Endpoint)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var sessions = provider.GetRequiredService<SessionsClient>();

        await Assert.That(sessions).IsNotNull();
    }

    [Test]
    public async Task AddOpenCode_Should_Apply_The_Configure_Action_When_Building_The_Client()
    {
        var configured = new List<OpenCodeClientOptions>();
        var services = new ServiceCollection();
        _ = services.AddOpenCode(options =>
        {
            options.Endpoint = Endpoint;
            configured.Add(options);
        });
        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<OpenCodeClient>();

        await Assert.That(configured).HasSingleItem();
    }

    [Test]
    public async Task AddOpenCode_Should_Bind_The_Configuration_Section_Including_Credentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(OpenCodeConfigurationData.ProtectedServer)
            .Build();
        using var handler = RecordingHttpHandler.RespondingJson(new FixtureLoader().LoadJson("known-health.json"));
        var services = new ServiceCollection();
        _ = services.AddOpenCode(configuration.GetSection("OpenCode"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<OpenCodeClient>().GetHealthAsync();

        var expected = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret"))}";
        await Assert.That(handler.Requests.Single().Authorization).IsEqualTo(expected);
    }

    [Test]
    public async Task AddOpenCode_Should_Compose_Delegating_Handlers_Through_The_Returned_Builder()
    {
        var payload = new FixtureLoader().LoadJson("known-health.json");
        using var handler = RecordingHttpHandler.RespondingJson(payload);
        using var witness = new WitnessHandler();
        var services = new ServiceCollection();
        _ = services.AddOpenCode(options => options.Endpoint = Endpoint)
            .AddHttpMessageHandler(() => witness)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<OpenCodeClient>().GetHealthAsync();

        await Assert.That(witness.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task AddOpenCode_Should_Refuse_An_Anonymous_Factory_Client_Carrying_A_Default_Authorization()
    {
        var services = new ServiceCollection();
        _ = services.AddOpenCode(options => options.Endpoint = Endpoint)
            .ConfigureHttpClient(static client =>
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "foreign-token"));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<ArgumentException>(() => _ = provider.GetRequiredService<OpenCodeClient>());

        await Assert.That(exception.Message).Contains("Authorization");
    }

    [Test]
    public async Task AddOpenCode_Should_Refuse_A_Factory_Client_Carrying_A_BaseAddress()
    {
        var services = new ServiceCollection();
        _ = services.AddOpenCode(options => options.Endpoint = Endpoint)
            .ConfigureHttpClient(static client => client.BaseAddress = new Uri("http://localhost:9"));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<ArgumentException>(() => _ = provider.GetRequiredService<OpenCodeClient>());

        await Assert.That(exception.Message).Contains("BaseAddress");
    }

    [Test]
    public async Task AddOpenCode_Should_Refuse_A_Null_Service_Collection()
    {
        _ = await Assert.That(() => ((IServiceCollection)null!).AddOpenCode(static _ => { }))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddOpenCode_Should_Refuse_A_Null_Configure_Action()
    {
        _ = await Assert.That(() => new ServiceCollection().AddOpenCode(configure: null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddOpenCode_Should_Refuse_A_Null_Configuration()
    {
        _ = await Assert.That(() => new ServiceCollection().AddOpenCode(configuration: null!))
            .Throws<ArgumentNullException>();
    }
}
