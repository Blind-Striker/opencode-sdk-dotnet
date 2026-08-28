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

    [Test]
    public async Task GetIntegrationAsync_Should_Return_The_Typed_Integration()
    {
        const string integration = "{\"id\":\"int_1\",\"name\":\"GitHub\",\"methods\":[],\"connections\":[]}";
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope(integration));

        var response = await scenario.Client.Integrations.GetIntegrationClient("int_1").GetIntegrationAsync();

        await Assert.That(response.Integration!.Id).IsEqualTo("int_1");
        await Assert.That(response.Integration.Name).IsEqualTo("GitHub");
        await Assert.That(response.Integration.Methods).IsEmpty();
        await Assert.That(response.Integration.Connections).IsEmpty();
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/integration/int_1"));
    }

    [Test]
    public async Task GetIntegrationAsync_Should_Return_Null_When_The_Integration_Is_Unknown()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope("null"));

        var response = await scenario.Client.Integrations.GetIntegrationClient("int_9").GetIntegrationAsync();

        await Assert.That(response.Integration).IsNull();
    }

    [Test]
    public async Task GetIntegrationAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Integrations.GetIntegrationClient("int_1").GetIntegrationAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task GetIntegrationAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Integrations.GetIntegrationClient("int_1")
            .GetIntegrationAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }
}
