using System.Globalization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The config family's live proof against the pinned server. The fixture isolates every global
/// root it resolves, so the preferences document the read and the patch address belongs to the
/// run, never to the developer profile. The shell list is a host observation: its rows vary by
/// machine, so the assertions are on the row shape the document declares, not on a spelling.
/// </summary>
[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class ConfigClientLiveTests(SimulatedDriveServerFixture server)
{
    [Test]
    [Timeout(60_000)]
    public async Task GetPreferencesAsync_Should_Return_The_Isolated_Global_Preferences(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var response = await client.Config.GetPreferencesAsync(cancellationToken: cancellationToken);

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        if (response.Preferences.Websearch is { } websearch)
        {
            await Assert.That(websearch.Kind).IsNotEqualTo(ConfigPreferencesWebsearchKind.Unknown);
        }

        Console.WriteLine(
            "config-live: preferences status=" + Number(response.Status) +
            " shell=" + (response.Preferences.Shell ?? "<unset>") +
            " websearch=" + (response.Preferences.Websearch?.Kind.ToString() ?? "<unset>"));
    }

    [Test]
    [Timeout(60_000)]
    public async Task GetShellsAsync_Should_Report_Acceptable_Host_Shells(CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var response = await client.Config.GetShellsAsync(cancellationToken: cancellationToken);

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Shells).IsNotEmpty();
        foreach (var shell in response.Shells)
        {
            await Assert.That(shell.Path).IsNotEmpty();
            await Assert.That(shell.Name).IsNotEmpty();
        }

        await Assert.That(response.Shells.Any(static shell => shell.Acceptable)).IsTrue();

        Console.WriteLine(
            "config-live: shells status=" + Number(response.Status) +
            " count=" + Number(response.Shells.Count) +
            " acceptable=" + Number(response.Shells.Count(static shell => shell.Acceptable)));
    }

    [Test]
    [Timeout(60_000)]
    public async Task PatchUpdatePreferencesAsync_Should_Persist_The_Disabled_Websearch_Arm(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var patched = await client.Config.PatchUpdatePreferencesAsync(
            new ConfigUpdatePreferencesPatchRequest
            {
                Websearch = ConfigPreferencesPatchWebsearch.FromBoolean(false),
            },
            cancellationToken: cancellationToken);

        await Assert.That(patched.Status).IsEqualTo(200);
        await Assert.That(patched.IsError).IsFalse();
        await Assert.That(patched.UpdatePreferences.Websearch!.Kind)
            .IsEqualTo(ConfigPreferencesWebsearchKind.Boolean);
        await Assert.That(patched.UpdatePreferences.Websearch.Boolean).IsFalse();

        var reread = await client.Config.GetPreferencesAsync(cancellationToken: cancellationToken);

        await Assert.That(reread.Status).IsEqualTo(200);
        await Assert.That(reread.Preferences.Websearch!.Kind).IsEqualTo(ConfigPreferencesWebsearchKind.Boolean);
        await Assert.That(reread.Preferences.Websearch.Boolean).IsFalse();

        Console.WriteLine(
            "config-live: patch status=" + Number(patched.Status) +
            " reread=" + Number(reread.Status) +
            " websearch=" + reread.Preferences.Websearch.Kind.ToString());
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
