using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The config family's pinned contract: three bare-payload operations whose documents declare
/// 200 beside 400 and 401 only. The websearch member is why the family needed a curated schema
/// name — the read and the patch carry two distinct structural unions over the same choice — so
/// both arms of both unions are asserted here.
/// </summary>
public sealed class ConfigClientContractTests
{
    private const string PreferencesBody = "{\"shell\":\"/bin/zsh\",\"websearch\":{\"provider\":\"exa\"}}";

    private const string ShellsBody =
        "[{\"path\":\"/bin/zsh\",\"name\":\"zsh\",\"acceptable\":true},"
        + "{\"path\":\"/bin/sh\",\"name\":\"sh\",\"acceptable\":false}]";

    [Test]
    public async Task GetPreferencesAsync_Should_Return_The_Bare_Typed_Preferences()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, PreferencesBody);

        var response = await scenario.Client.Config.GetPreferencesAsync();

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Preferences.Shell).IsEqualTo("/bin/zsh");
        await Assert.That(response.Preferences.Websearch!.Kind)
            .IsEqualTo(ConfigPreferencesWebsearchKind.ConfigWebSearchInfo);
        await Assert.That(response.Preferences.Websearch.ConfigWebSearchInfo.Provider).IsEqualTo("exa");
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/config/preferences"));
    }

    [Test]
    public async Task GetPreferencesAsync_Should_Carry_The_Disabled_Websearch_Arm()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "{\"websearch\":false}");

        var response = await scenario.Client.Config.GetPreferencesAsync();

        await Assert.That(response.Preferences.Shell).IsNull();
        await Assert.That(response.Preferences.Websearch!.Kind).IsEqualTo(ConfigPreferencesWebsearchKind.Boolean);
        await Assert.That(response.Preferences.Websearch.Boolean).IsFalse();
    }

    [Test]
    public async Task GetPreferencesAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Config.GetPreferencesAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task GetPreferencesAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Config.GetPreferencesAsync(OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }

    [Test]
    public async Task GetShellsAsync_Should_Return_The_Bare_Typed_List()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, ShellsBody);

        var response = await scenario.Client.Config.GetShellsAsync();

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.Shells.Count).IsEqualTo(2);
        await Assert.That(response.Shells[0].Name).IsEqualTo("zsh");
        await Assert.That(response.Shells[0].Path).IsEqualTo("/bin/zsh");
        await Assert.That(response.Shells[0].Acceptable).IsTrue();
        await Assert.That(response.Shells[1].Acceptable).IsFalse();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/config/shell"));
    }

    [Test]
    public async Task GetShellsAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Config.GetShellsAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task GetShellsAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Config.GetShellsAsync(OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }

    [Test]
    public async Task PatchUpdatePreferencesAsync_Should_Send_The_Typed_Patch_Body()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, PreferencesBody);

        var response = await scenario.Client.Config.PatchUpdatePreferencesAsync(new ConfigUpdatePreferencesPatchRequest
        {
            Shell = "/bin/zsh",
            Websearch = ConfigPreferencesPatchWebsearch.FromConfigWebSearchInfo(new ConfigWebSearchInfo
            {
                Provider = "exa",
            }),
        });

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.UpdatePreferences.Shell).IsEqualTo("/bin/zsh");
        var request = scenario.Requests.Single();
        await Assert.That(request.Method.Method).IsEqualTo("PATCH");
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/config/preferences"));
        await Assert.That(request.Body).IsEqualTo("{\"shell\":\"/bin/zsh\",\"websearch\":{\"provider\":\"exa\"}}");
    }

    [Test]
    public async Task PatchUpdatePreferencesAsync_Should_Send_The_Disabled_Websearch_Arm()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "{}");

        var response = await scenario.Client.Config.PatchUpdatePreferencesAsync(new ConfigUpdatePreferencesPatchRequest
        {
            Websearch = ConfigPreferencesPatchWebsearch.FromBoolean(false),
        });

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.UpdatePreferences.Websearch).IsNull();
        await Assert.That(scenario.Requests.Single().Body).IsEqualTo("{\"websearch\":false}");
    }

    [Test]
    public async Task PatchUpdatePreferencesAsync_Should_Send_An_Empty_Body_When_The_Patch_Is_Omitted()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "{}");

        _ = await scenario.Client.Config.PatchUpdatePreferencesAsync();

        await Assert.That(scenario.Requests.Single().Body).IsEqualTo("{}");
    }

    [Test]
    public async Task PatchUpdatePreferencesAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Config.PatchUpdatePreferencesAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task PatchUpdatePreferencesAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Config.PatchUpdatePreferencesAsync(
            request: null,
            OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }
}
