using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class VcsClientContractTests
{
    [Test]
    public async Task GetStatusAsync_Should_Return_The_Typed_Changes_With_Their_Location()
    {
        const string changes = "[{\"file\":\"a.txt\",\"additions\":3,\"deletions\":0,\"status\":\"added\"},"
            + "{\"file\":\"b.txt\",\"additions\":1,\"deletions\":2,\"status\":\"modified\"}]";
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope(changes));

        var response = await scenario.Client.Vcs.GetStatusAsync();

        await Assert.That(response.Changes.Count).IsEqualTo(2);
        await Assert.That(response.Changes[0].File).IsEqualTo("a.txt");
        await Assert.That(response.Changes[0].Status).IsEqualTo(VcsFileStatusStatus.Added);
        await Assert.That(response.Changes[1].File).IsEqualTo("b.txt");
        await Assert.That(response.Changes[1].Additions).IsEqualTo(1);
        await Assert.That(response.Changes[1].Deletions).IsEqualTo(2);
        await Assert.That(response.Changes[1].Status).IsEqualTo(VcsFileStatusStatus.Modified);
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/vcs/status"));
    }

    [Test]
    public async Task GetStatusAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Vcs.GetStatusAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task GetStatusAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Vcs.GetStatusAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }

    [Test]
    public async Task GetBranchesAsync_Should_Return_The_Typed_Branches_With_Their_Location()
    {
        using var scenario = ContractScenario.Responding(
            HttpStatusCode.OK, WireBodyData.LocationEnvelope("[\"main\",\"dev\"]"));

        var response = await scenario.Client.Vcs.GetBranchesAsync();

        await Assert.That(response.Branches.Count).IsEqualTo(2);
        await Assert.That(response.Branches[0]).IsEqualTo("main");
        await Assert.That(response.Branches[1]).IsEqualTo("dev");
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/vcs/branches"));
    }

    [Test]
    public async Task GetBranchesAsync_Should_Return_An_Empty_List_With_Its_Location()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope("[]"));

        var response = await scenario.Client.Vcs.GetBranchesAsync();

        await Assert.That(response.Branches.Count).IsEqualTo(0);
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
    }

    [Test]
    public async Task GetBranchesAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Vcs.GetBranchesAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task GetBranchesAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Vcs.GetBranchesAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }
}
