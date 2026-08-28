using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class ProjectsClientContractTests
{
    [Test]
    public async Task GetCurrentAsync_Should_Return_The_Typed_Project()
    {
        using var scenario = ContractScenario.Responding(
            HttpStatusCode.OK,
            "{\"id\":\"prj_1\",\"directory\":\"/repo\",\"canonical\":\"/repo\"}");

        var response = await scenario.Client.Projects.GetCurrentAsync();

        await Assert.That(response.Current.Id).IsEqualTo("prj_1");
        await Assert.That(response.Current.Directory).IsEqualTo("/repo");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/project/current"));
    }

    [Test]
    public async Task ListProjectsAsync_Should_Return_The_Typed_Projects()
    {
        const string body = "[{\"id\":\"prj_1\",\"canonical\":\"/repo\",\"time\":{\"created\":1,\"updated\":2},\"sandboxes\":[]}]";
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, body);

        var response = await scenario.Client.Projects.ListProjectsAsync();

        var project = response.Projects.Single();
        await Assert.That(project.Id).IsEqualTo("prj_1");
        await Assert.That(project.Canonical).IsEqualTo("/repo");
        await Assert.That(project.Time.Created).IsEqualTo(1);
        await Assert.That(project.Sandboxes).IsEmpty();
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/project"));
    }

    [Test]
    public async Task ListProjectsAsync_Should_Return_An_Empty_List()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "[]");

        var response = await scenario.Client.Projects.ListProjectsAsync();

        await Assert.That(response.Projects).IsEmpty();
    }

    [Test]
    public async Task ListProjectsAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Projects.ListProjectsAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task ListProjectsAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Projects.ListProjectsAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }
}
