using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class WorkspacesClientContractTests
{
    [Test]
    public async Task CreateWorkspaceAsync_Should_Send_The_Typed_Body_And_Return_The_Workspace_Id()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "{\"data\":\"wrk_1\"}");

        var response = await scenario.Client.Workspaces.CreateWorkspaceAsync(new WorkspaceCreateRequest
        {
            Provider = "docker",
        });

        await Assert.That(response.Workspace).IsEqualTo("wrk_1");
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/workspace"));
        await Assert.That(request.Body).IsEqualTo("{\"provider\":\"docker\"}");
    }

    [Test]
    public async Task CreateWorkspaceAsync_Should_Send_The_Caller_Supplied_Id_When_Present()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "{\"data\":\"wrk_1\"}");

        _ = await scenario.Client.Workspaces.CreateWorkspaceAsync(new WorkspaceCreateRequest
        {
            Id = "wrk_1",
            Provider = "docker",
        });

        await Assert.That(scenario.Requests.Single().Body).IsEqualTo("{\"id\":\"wrk_1\",\"provider\":\"docker\"}");
    }

    [Test]
    public async Task CreateWorkspaceAsync_Should_Throw_The_Declared_404_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.ProviderNotFoundError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Workspaces.CreateWorkspaceAsync(new WorkspaceCreateRequest { Provider = "docker" }))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(404);
        await Assert.That(exception.Error).IsTypeOf<ProviderNotFoundError>();
    }

    [Test]
    public async Task CreateWorkspaceAsync_Should_Throw_The_Declared_409_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Conflict, WireBodyData.ConflictError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Workspaces.CreateWorkspaceAsync(new WorkspaceCreateRequest { Provider = "docker" }))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(409);
        await Assert.That(exception.Error).IsTypeOf<ConflictError>();
    }

    [Test]
    public async Task CreateWorkspaceAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Workspaces.CreateWorkspaceAsync(
            new WorkspaceCreateRequest { Provider = "docker" },
            OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }
}
