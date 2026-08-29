using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class ExternalServerEndpointTests
{
    [Test]
    public async Task FromEnvironment_Should_Return_The_Pair_When_Both_Variables_Are_Set()
    {
        var endpoint = ExternalServerEndpoint.FromEnvironment(name => name switch
        {
            "OPENCODE_SDK_TESTS_ENDPOINT" => "http://localhost:4097",
            "OPENCODE_SDK_TESTS_PASSWORD" => "secret",
            _ => null,
        });

        await Assert.That(endpoint).IsNotNull();
        await Assert.That(endpoint!.Endpoint).IsEqualTo(new Uri("http://localhost:4097"));
        await Assert.That(endpoint.Password).IsEqualTo("secret");
    }

    [Test]
    public async Task FromEnvironment_Should_Return_Null_When_Neither_Variable_Is_Set()
    {
        var endpoint = ExternalServerEndpoint.FromEnvironment(static _ => null);

        await Assert.That(endpoint).IsNull();
    }

    [Test]
    [Arguments("OPENCODE_SDK_TESTS_ENDPOINT")]
    [Arguments("OPENCODE_SDK_TESTS_PASSWORD")]
    public async Task FromEnvironment_Should_Throw_Naming_Both_Variables_When_Only_One_Is_Set(string onlySet)
    {
        string? Read(string name) => name == onlySet ? "value" : null;

        var exception = await Assert
            .That(() => ExternalServerEndpoint.FromEnvironment(Read))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("OPENCODE_SDK_TESTS_ENDPOINT");
        await Assert.That(exception.Message).Contains("OPENCODE_SDK_TESTS_PASSWORD");
    }

    [Test]
    public async Task FromEnvironment_Should_Throw_Naming_The_Variable_When_The_Endpoint_Does_Not_Parse()
    {
        static string? Read(string name) => name switch
        {
            "OPENCODE_SDK_TESTS_ENDPOINT" => "not-a-uri",
            "OPENCODE_SDK_TESTS_PASSWORD" => "secret",
            _ => null,
        };

        var exception = await Assert
            .That(() => ExternalServerEndpoint.FromEnvironment(Read))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("OPENCODE_SDK_TESTS_ENDPOINT");
    }
}
