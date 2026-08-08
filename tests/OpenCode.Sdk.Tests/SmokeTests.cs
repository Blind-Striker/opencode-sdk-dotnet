namespace OpenCode.Sdk.Tests;

public sealed class SmokeTests
{
    [Test]
    public async Task TestInfrastructure_Should_Run_On_Every_Target_Framework()
    {
        // Placeholder until the first real SDK code lands; keeps the TUnit +
        // Microsoft.Testing.Platform pipeline exercised on every TFM and OS leg.
        await Assert.That(Environment.Version).IsNotNull();
    }
}
