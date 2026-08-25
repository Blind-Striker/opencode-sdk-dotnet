using System.Net;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class PermissionsClientContractTests
{
    [Test]
    public async Task ListRequestsAsync_Should_Return_The_Typed_Pending_Requests()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-permission-request.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope($"[{payload}]"));

        var response = await scenario.Client.Permissions.ListRequestsAsync();

        var request = response.Requests.Single();
        await Assert.That(request.Id).IsEqualTo("per_1");
        await Assert.That(request.Action).IsEqualTo("read");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/permission/request"));
    }

    [Test]
    public async Task RemoveSavedAsync_Should_Take_The_Id_As_An_Argument_On_The_204()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NoContent, string.Empty);

        var response = await scenario.Client.Permissions.RemoveSavedAsync("per_9");

        await Assert.That(response.Status).IsEqualTo(204);
        await Assert.That(response.IsError).IsFalse();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Delete);
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/permission/saved/per_9"));
    }
}
