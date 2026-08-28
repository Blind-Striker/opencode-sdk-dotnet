using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class IntegrationsClientContractTests
{
    [Test]
    [Arguments(" ")]
    [Arguments(".")]
    [Arguments("..")]
    public async Task GetIntegrationClient_Should_Refuse_An_Id_The_Route_Would_Refuse(string integrationId)
    {
        using var scenario = ContractScenario.Responding();

        var exception = Assert.Throws<ArgumentException>(() => _ = scenario.Client.Integrations.GetIntegrationClient(integrationId));

        await Assert.That(exception.ParamName).IsEqualTo("integrationId");
    }
}
