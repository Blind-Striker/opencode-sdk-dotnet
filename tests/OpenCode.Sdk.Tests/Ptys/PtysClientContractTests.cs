using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class PtysClientContractTests
{
    [Test]
    public async Task CreatePtyAsync_Should_Send_The_Typed_Body_And_Return_The_Typed_Pty()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-pty.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope(payload));

        var response = await scenario.Client.Ptys.CreatePtyAsync(new PtyCreateRequest
        {
            Command = "pwsh",
            Title = "probe shell",
        });

        await Assert.That(response.Pty.Id).IsEqualTo("pty_100");
        await Assert.That(response.Pty.Status).IsEqualTo(PtyStatus.Running);
        await Assert.That(response.Pty.Pid).IsEqualTo(4242);
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/pty"));
        await Assert.That(request.Body).IsEqualTo("{\"command\":\"pwsh\",\"title\":\"probe shell\"}");
    }

    [Test]
    public async Task GetPtyAsync_Should_Throw_The_Declared_404_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.PtyNotFoundError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Ptys.GetPtyClient("pty_9").GetPtyAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(404);
        await Assert.That(exception.Error).IsTypeOf<PtyNotFoundError>();
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/pty/pty_9"));
    }
}
