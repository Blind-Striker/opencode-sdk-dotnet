using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class CredentialsClientContractTests
{
    [Test]
    public async Task UpdateCredentialAsync_Should_Send_The_Patch_Body_On_The_Flat_Id_Route()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NoContent, string.Empty);

        var response = await scenario.Client.Credentials.UpdateCredentialAsync("cred_1", new CredentialUpdatePatchRequest
        {
            Label = "work laptop",
        });

        await Assert.That(response.Status).IsEqualTo(204);
        await Assert.That(response.IsError).IsFalse();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method.Method).IsEqualTo("PATCH");
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/credential/cred_1"));
        await Assert.That(request.Body).IsEqualTo("{\"label\":\"work laptop\"}");
    }
}
