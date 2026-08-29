using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class FormsClientContractTests
{
    [Test]
    public async Task ListRequestsAsync_Should_Return_The_Typed_Pending_Requests()
    {
        const string form = "{\"id\":\"frm_1\",\"sessionID\":\"ses_1\",\"title\":\"Pick a provider\","
            + "\"fields\":[{\"key\":\"provider\",\"type\":\"string\"}]}";
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope($"[{form}]"));

        var response = await scenario.Client.Forms.ListRequestsAsync();

        var request = response.Requests.Single();
        await Assert.That(request.Id).IsEqualTo("frm_1");
        await Assert.That(request.SessionId).IsEqualTo("ses_1");
        await Assert.That(request.Title).IsEqualTo("Pick a provider");
        var field = (FormStringField)request.Fields.Single();
        await Assert.That(field.Key).IsEqualTo("provider");
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/form/request"));
    }

    [Test]
    public async Task ListRequestsAsync_Should_Return_An_Empty_List()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope("[]"));

        var response = await scenario.Client.Forms.ListRequestsAsync();

        await Assert.That(response.Requests).IsEmpty();
    }

    [Test]
    public async Task ListRequestsAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Forms.ListRequestsAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task ListRequestsAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Forms.ListRequestsAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }
}
