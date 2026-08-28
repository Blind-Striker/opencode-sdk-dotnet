using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class ProjectWorktreesClientContractTests
{
    [Test]
    public async Task ListWorktreesAsync_Should_Return_The_Typed_Worktrees()
    {
        const string worktrees = "[{\"directory\":\"/repo\",\"strategy\":\"branch\"},{\"directory\":\"/repo-2\"}]";
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, worktrees);

        var response = await scenario.Client.Worktrees.GetProjectWorktreesClient("prj_1").ListWorktreesAsync();

        await Assert.That(response.Worktrees.Count).IsEqualTo(2);
        await Assert.That(response.Worktrees[0].Directory).IsEqualTo("/repo");
        await Assert.That(response.Worktrees[0].Strategy).IsEqualTo("branch");
        await Assert.That(response.Worktrees[1].Directory).IsEqualTo("/repo-2");
        await Assert.That(response.Worktrees[1].Strategy).IsNull();
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/worktree/prj_1"));
    }

    [Test]
    public async Task ListWorktreesAsync_Should_Return_An_Empty_List()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "[]");

        var response = await scenario.Client.Worktrees.GetProjectWorktreesClient("prj_1").ListWorktreesAsync();

        await Assert.That(response.Worktrees).IsEmpty();
    }

    [Test]
    public async Task ListWorktreesAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Worktrees.GetProjectWorktreesClient("prj_1").ListWorktreesAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task ListWorktreesAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Worktrees.GetProjectWorktreesClient("prj_1")
            .ListWorktreesAsync(OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }
}
