using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class IntegrationClientContractTests
{
    [Test]
    public async Task GetCommandStatusAsync_Should_Materialize_The_Tagged_Status_Behind_Both_Path_Parameters()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-command-attempt-complete.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope(payload));

        var response = await scenario.Client.Integrations.GetIntegrationClient("int_1").GetCommandStatusAsync("att_1");

        await Assert.That(response.CommandStatus).IsTypeOf<IntegrationCommandAttemptStatusComplete>();
        var complete = (IntegrationCommandAttemptStatusComplete)response.CommandStatus;
        await Assert.That(complete.Time.Created).IsEqualTo(1755200000);
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/integration/int_1/connect/command/att_1"));
    }
}
