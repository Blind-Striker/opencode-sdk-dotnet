using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

[ClassDataSource<PinnedOpenCodeServerFixture>(Shared = SharedType.PerTestSession)]
public sealed class PinnedServerFixtureTests(PinnedOpenCodeServerFixture server)
{
    [Test]
    [Timeout(60_000)]
    public async Task Fixture_Should_Answer_Health_Through_Its_Own_Client(CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var health = await client.GetHealthAsync(cancellationToken: cancellationToken);

        await Assert.That(health.Health.Healthy).IsTrue();
    }

    [Test]
    public async Task Fixture_Should_Hand_Out_Isolated_Workspaces()
    {
        using var first = server.CreateWorkspace();
        using var second = server.CreateWorkspace();

        await Assert.That(first.Path).IsNotEqualTo(second.Path);
        await Assert.That(first.Path).Contains("workspaces");
    }
}
