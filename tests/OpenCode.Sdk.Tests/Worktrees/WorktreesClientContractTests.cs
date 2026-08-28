using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class WorktreesClientContractTests
{
    [Test]
    [Arguments(" ")]
    [Arguments(".")]
    [Arguments("..")]
    public async Task GetProjectWorktreesClient_Should_Refuse_An_Id_The_Route_Would_Refuse(string projectId)
    {
        using var scenario = ContractScenario.Responding();

        var exception = Assert.Throws<ArgumentException>(() => _ = scenario.Client.Worktrees.GetProjectWorktreesClient(projectId));

        await Assert.That(exception.ParamName).IsEqualTo("projectId");
    }
}
