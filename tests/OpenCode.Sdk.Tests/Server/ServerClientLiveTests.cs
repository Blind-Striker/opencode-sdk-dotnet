using System.Globalization;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class ServerClientLiveTests(SimulatedDriveServerFixture server)
{
    [Test]
    [Timeout(60_000)]
    public async Task GetServerAsync_Should_Report_The_Reachable_Fixture_Endpoint(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var response = await client.Server.GetServerAsync(cancellationToken: cancellationToken);

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Urls).IsNotEmpty();
        foreach (var value in response.Urls)
        {
            var isAbsolute = Uri.TryCreate(value, UriKind.Absolute, out var url);
            await Assert.That(isAbsolute).IsTrue();
            await Assert.That(url).IsNotNull();
            await Assert.That(
                string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)).IsTrue();
            await Assert.That(url.Port).IsGreaterThan(0);
            await Assert.That(url.IsDefaultPort).IsFalse();
        }

        await Assert.That(response.Urls.Any(value =>
            Uri.TryCreate(value, UriKind.Absolute, out var url) && url == server.Endpoint)).IsTrue();

        Console.WriteLine(
            "server-live: status=" + Number(response.Status) +
            " endpoint=" + server.Endpoint +
            " urls=" + Number(response.Urls.Count));
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
