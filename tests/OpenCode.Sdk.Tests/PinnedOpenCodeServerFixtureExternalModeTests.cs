using System.Net;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// Pins the exact-pin fixture's external-endpoint mode (Task 6): attaching to an
/// operator-supplied server instead of spawning one, and refusing fail-fast when that server does
/// not answer a health probe. Drives the fixture through its internal
/// <see cref="ExternalServerEndpoint"/> constructor over a real loopback socket, not the shared
/// per-session instance <see cref="PinnedServerFixtureTests"/> shares.
/// </summary>
public sealed class PinnedOpenCodeServerFixtureExternalModeTests
{
    [Test]
    [Timeout(15_000)]
    public async Task Fixture_Should_Attach_To_An_External_Endpoint_When_One_Is_Supplied(CancellationToken cancellationToken)
    {
        await using var server = LoopbackHttpServer.Start(static path => path switch
        {
            "/api/health" => new LoopbackHttpResponse
            {
                StatusCode = HttpStatusCode.OK,
                ContentType = "application/json",
                Body = WireBodyData.HealthOk,
            },
            _ => new LoopbackHttpResponse { StatusCode = HttpStatusCode.InternalServerError },
        });
        var fixture = new PinnedOpenCodeServerFixture(new ExternalServerEndpoint(server.Endpoint, "any-password"));

        await fixture.InitializeAsync();
        try
        {
            await Assert.That(fixture.Endpoint).IsEqualTo(server.Endpoint);
            using var client = fixture.CreateClient();

            var health = await client.GetHealthAsync(cancellationToken: cancellationToken);

            await Assert.That(health.Health.Healthy).IsTrue();
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Test]
    public async Task Fixture_Should_Refuse_An_External_Endpoint_That_Does_Not_Answer_Health()
    {
        await using var server = LoopbackHttpServer.Start(
            static _ => new LoopbackHttpResponse { StatusCode = HttpStatusCode.InternalServerError });
        var fixture = new PinnedOpenCodeServerFixture(new ExternalServerEndpoint(server.Endpoint, "any-password"));

        var exception = await Assert.That(fixture.InitializeAsync).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains(server.Endpoint.ToString());
        await fixture.DisposeAsync();
    }
}
