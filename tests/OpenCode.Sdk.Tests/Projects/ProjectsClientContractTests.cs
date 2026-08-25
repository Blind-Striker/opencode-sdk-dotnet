using System.Net;
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
}
