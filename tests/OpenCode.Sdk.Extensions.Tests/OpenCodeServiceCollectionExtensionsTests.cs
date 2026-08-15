using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCode.Sdk.Extensions.Tests.Support;

namespace OpenCode.Sdk.Extensions.Tests;

public sealed class OpenCodeServiceCollectionExtensionsTests
{
    private static readonly Uri Endpoint = new("http://localhost:4096");

    [Test]
    public async Task AddOpenCode_Should_Resolve_One_Singleton_Client()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<OpenCodeClient>();
        var second = provider.GetRequiredService<OpenCodeClient>();

        await Assert.That(first).IsSameReferenceAs(second);
    }

    [Test]
    public async Task AddOpenCode_Should_Resolve_The_Sessions_Client_From_The_Same_Root_Instance()
    {
        using var provider = BuildProvider();

        var root = provider.GetRequiredService<OpenCodeClient>();
        var sessions = provider.GetRequiredService<SessionsClient>();

        await Assert.That(sessions).IsSameReferenceAs(root.Sessions);
    }

    [Test]
    public async Task AddOpenCode_Should_Register_Every_Root_Client_Family()
    {
        using var provider = BuildProvider();
        var root = provider.GetRequiredService<OpenCodeClient>();
        var families = typeof(OpenCodeClient).GetProperties()
            .Where(property => property.PropertyType.Name.EndsWith("Client", StringComparison.Ordinal))
            .ToList();

        await Assert.That(families).IsNotEmpty();
        foreach (var family in families)
        {
            var resolved = provider.GetService(family.PropertyType);
            await Assert.That(resolved).IsNotNull();
            await Assert.That(resolved).IsSameReferenceAs(family.GetValue(root));
        }
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
        var services = new ServiceCollection();
        _ = services.AddOpenCode(configuration.GetSection("OpenCode"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<OpenCodeClientOptions>>().Value;

        await Assert.That(options.Endpoint).IsEqualTo(Endpoint);
        await Assert.That(options.Username).IsEqualTo("admin");
        await Assert.That(options.Password).IsEqualTo("secret");
    }

    [Test]
    public async Task AddOpenCode_Should_Return_The_Service_Collection_For_Chaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddOpenCode(static options => options.Endpoint = Endpoint);

        await Assert.That(returned).IsSameReferenceAs(services);
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

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        _ = services.AddOpenCode(static options => options.Endpoint = Endpoint);
        return services.BuildServiceProvider();
    }
}
