using System.Text;
using OpenCode.Sdk.Internal.BackgroundService;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// The strict boundary over the CLI's service config: the five declared members are validated, the
/// persisted password is accepted and never surfaced, and only the environment map comes out.
/// </summary>
public sealed class ServiceConfigReaderTests
{
    [Test]
    public async Task TryReadEnvironment_Should_Return_The_Environment_Map_Of_The_Pinned_Config()
    {
        var environment = ServiceConfigReader.TryReadEnvironment(Bytes(new FixtureLoader().LoadJson("BackgroundService.service-config-env.json")));

        await Assert.That(environment).IsNotNull();
        await Assert.That(environment.Count).IsEqualTo(2);
        await Assert.That(environment["OPENCODE_DISABLE_MODELS_FETCH"]).IsEqualTo("1");
        await Assert.That(environment["HTTPS_PROXY"]).IsEqualTo("http://proxy.internal:3128");
        await Assert.That(environment.Values).DoesNotContain(ServiceConfigData.ConfigPassword);
    }

    [Test]
    [Arguments(ServiceConfigData.Empty)]
    [Arguments(ServiceConfigData.HostnameOnly)]
    [Arguments(ServiceConfigData.PortAtLowerBound)]
    [Arguments(ServiceConfigData.PortAtUpperBound)]
    public async Task TryReadEnvironment_Should_Accept_A_Valid_Config_Without_Environment_As_Empty(string json)
    {
        var environment = ServiceConfigReader.TryReadEnvironment(Bytes(json));

        await Assert.That(environment).IsNotNull();
        await Assert.That(environment).IsEmpty();
    }

    [Test]
    public async Task TryReadEnvironment_Should_Skip_Unknown_Members()
    {
        var environment = ServiceConfigReader.TryReadEnvironment(Bytes(ServiceConfigData.UnknownMembersSkipped));

        await Assert.That(environment).IsNotNull();
        await Assert.That(environment["A"]).IsEqualTo("1");
    }

    [Test]
    [Arguments(ServiceConfigData.PortZero)]
    [Arguments(ServiceConfigData.PortAboveRange)]
    [Arguments(ServiceConfigData.PortAsString)]
    [Arguments(ServiceConfigData.CorsNotAnArray)]
    [Arguments(ServiceConfigData.CorsWithNonString)]
    [Arguments(ServiceConfigData.EnvWithNonStringValue)]
    [Arguments(ServiceConfigData.EnvNotAnObject)]
    [Arguments(ServiceConfigData.HostnameNotAString)]
    [Arguments(ServiceConfigData.ArrayRoot)]
    [Arguments(ServiceConfigData.Malformed)]
    public async Task TryReadEnvironment_Should_Treat_An_Invalid_Config_As_Absent(string json)
    {
        var environment = ServiceConfigReader.TryReadEnvironment(Bytes(json));

        await Assert.That(environment).IsNull();
    }

    /// <summary>A hand-edited config saved by an editor that writes a BOM: the CLI's decoder strips it, so its <c>env</c> applies.</summary>
    [Test]
    public async Task TryReadEnvironment_Should_Read_A_Bom_Prefixed_Config()
    {
        var environment = ServiceConfigReader.TryReadEnvironment([0xEF, 0xBB, 0xBF, .. Bytes("{\"env\":{\"A\":\"1\"}}")]);

        await Assert.That(environment).IsNotNull();
        await Assert.That(environment["A"]).IsEqualTo("1");
    }

    [Test]
    public async Task TryReadEnvironment_Should_Treat_Invalid_Utf8_In_A_String_As_Absent()
    {
        // 0xC3 opens a two-byte sequence that 0x28 ('(') cannot continue.
        byte[] document = [.. Bytes("{\"env\":{\"A\":\""), 0xC3, 0x28, .. Bytes("\"}}")];

        var environment = ServiceConfigReader.TryReadEnvironment(document);

        await Assert.That(environment).IsNull();
    }

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);
}
