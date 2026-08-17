using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class ShellClientContractTests
{
    [Test]
    public async Task GetShellAsync_Should_Return_The_Typed_Shell_With_Its_Location()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-shell.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope(payload));

        var response = await scenario.Client.Shells.GetShellClient("sh_100").GetShellAsync();

        await Assert.That(response.Shell.Id).IsEqualTo("sh_100");
        await Assert.That(response.Shell.Time.Started).IsEqualTo(1755200000);
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/shell/sh_100"));
    }

    [Test]
    public async Task RemoveShellAsync_Should_Treat_The_204_As_A_Bodiless_Success()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NoContent, string.Empty);

        var response = await scenario.Client.Shells.GetShellClient("sh_100").RemoveShellAsync();

        await Assert.That(response.Status).IsEqualTo(204);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.RawBody).IsNull();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Delete);
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/shell/sh_100"));
    }

    [Test]
    public async Task RemoveShellAsync_Should_Ignore_A_Body_On_The_Declared_204()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NoContent, "{}");

        var response = await scenario.Client.Shells.GetShellClient("sh_100").RemoveShellAsync();

        await Assert.That(response.Status).IsEqualTo(204);
        await Assert.That(response.IsError).IsFalse();
    }

    [Test]
    public async Task RemoveShellAsync_Should_Treat_A_200_As_An_Undeclared_Success()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "{}");

        _ = await Assert
            .That(async () => _ = await scenario.Client.Shells.GetShellClient("sh_100").RemoveShellAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task TimeoutShellAsync_Should_Send_The_Patch_Body_Beside_The_Location_Query()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-shell.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope(payload));

        _ = await scenario.Client.Shells.GetShellClient("sh_100").TimeoutShellAsync(new ShellTimeoutRequest
        {
            Timeout = 9000,
            Location = new LocationSelector { Workspace = "wrk_1" },
        });

        var request = scenario.Requests.Single();
        await Assert.That(request.Method.Method).IsEqualTo("PATCH");
        await Assert.That(request.RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/shell/sh_100/timeout?location[workspace]=wrk_1");
        await Assert.That(request.Body).IsEqualTo("{\"timeout\":9000}");
    }

    [Test]
    public async Task GetShellAsync_Should_Throw_The_Declared_404_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.ShellNotFoundError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Shells.GetShellClient("sh_9").GetShellAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(404);
        await Assert.That(exception.Error).IsTypeOf<ShellNotFoundError>();
    }
}
