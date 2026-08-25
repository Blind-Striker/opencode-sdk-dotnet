using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class McpServersClientContractTests
{
    [Test]
    public async Task ListMcpServersAsync_Should_Return_The_Typed_Servers()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-mcp-server.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope($"[{payload}]"));

        var response = await scenario.Client.McpServers.ListMcpServersAsync();

        var server = response.McpServers.Single();
        await Assert.That(server.Name).IsEqualTo("docs");
        await Assert.That(server.Status).IsTypeOf<McpStatusConnected>();
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/mcp"));
    }

    [Test]
    public async Task RemoveMcpServerAsync_Should_Throw_The_Declared_404_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.McpServerNotFoundError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.McpServers.GetMcpServerClient("docs").RemoveMcpServerAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(404);
        await Assert.That(exception.Error).IsTypeOf<McpServerNotFoundError>();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Delete);
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/mcp/docs"));
    }
}
