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

    /// <summary>
    /// The three states against the real server, on the fixture's own isolated global roots.
    /// Upstream's preferences patch is where an explicit null carries service meaning: it deletes
    /// the key rather than storing null, and an omitted member leaves the stored value alone. This
    /// is the behaviour the tri-state request member exists for.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task PatchUpdatePreferencesAsync_Should_Set_Keep_And_Clear_The_Shell_Preference(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();
        var shells = await client.Config.GetShellsAsync(cancellationToken: cancellationToken);
        var shell = shells.Shells.First(static candidate => candidate.Acceptable).Path;

        var set = await client.Config.PatchUpdatePreferencesAsync(
            new ConfigUpdatePreferencesPatchRequest { Shell = shell, },
            cancellationToken: cancellationToken);

        await Assert.That(set.Status).IsEqualTo(200);
        await Assert.That(set.UpdatePreferences.Shell).IsEqualTo(shell);

        // An unassigned member is absent: the SDK sends '{}' and the stored value survives.
        var kept = await client.Config.PatchUpdatePreferencesAsync(
            new ConfigUpdatePreferencesPatchRequest(),
            cancellationToken: cancellationToken);
        var afterKeep = await client.Config.GetPreferencesAsync(cancellationToken: cancellationToken);

        await Assert.That(kept.UpdatePreferences.Shell).IsEqualTo(shell);
        await Assert.That(afterKeep.Preferences.Shell).IsEqualTo(shell);

        // An explicit null is the delete; the read back no longer carries the key at all.
        var cleared = await client.Config.PatchUpdatePreferencesAsync(
            new ConfigUpdatePreferencesPatchRequest { Shell = Optional<string?>.Null, },
            cancellationToken: cancellationToken);
        var afterClear = await client.Config.GetPreferencesAsync(cancellationToken: cancellationToken);

        await Assert.That(cleared.Status).IsEqualTo(200);
        await Assert.That(cleared.UpdatePreferences.Shell).IsNull();
        await Assert.That(afterClear.Preferences.Shell).IsNull();

        Console.WriteLine(
            "config-live: tristate set=" + Number(set.Status) +
            " keep=" + Number(kept.Status) +
            " clear=" + Number(cleared.Status) +
            " shell=" + shell +
            " afterClear=" + (afterClear.Preferences.Shell ?? "<unset>"));
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
