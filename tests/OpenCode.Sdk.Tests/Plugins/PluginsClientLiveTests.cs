using System.Globalization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The plugin family's deterministic arms against the pinned server: activation settles (204),
/// the owned server's inventory contains its known builtins plus the repository-owned local RPC
/// plugin, and <c>plugin.check</c> / <c>plugin.update</c> answer their inventory-only arms - no
/// target (200), an empty target list (204), and a target outside the inventory (400). Every
/// check/update test proves its activated inventory has no package source before it invokes the
/// operation, so none of these calls can reach a registry.
/// </summary>
/// <remarks>
/// Deliberately not covered, because they reach the real npm registry, which no test may touch
/// (ADR-0022): <c>plugin.check</c>'s <c>outdated</c> flag needs a package-sourced plugin whose
/// version pacote resolves online (<c>packages/util/src/npm.ts:356-380</c>, <c>preferOnline</c>),
/// and <c>plugin.update</c>'s 503 <c>ServiceUnavailableError</c> (<c>service: "plugin"</c>) needs
/// such a package's update to fail. Simulation replaces only Effect's HttpClient, not pacote's own
/// HTTP, and the pin has no switch to stub Npm, so those arms are unreachable deterministically and
/// are named here rather than skipped. Also not covered: the <c>sdk</c>-sourced inventory entry.
/// It exists only in simulation, and only once the simulated network layer has been built - the
/// simulation backend registers its tool plugin while constructing the provider that layer
/// depends on (<c>simulated-provider.ts:272</c> under <c>backend/index.ts:33-57</c>), which the
/// first model turn triggers, not the boot. A cold simulated server lists builtins only
/// (observed live), so that arm belongs after a scripted turn, not in a standalone list.
/// </remarks>
[ClassDataSource<PinnedOpenCodeServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class PluginsClientLiveTests(PinnedOpenCodeServerFixture server)
{
    /// <summary>A package target no inventory at the pin contains; the server's message must name it back.</summary>
    private const string AbsentTarget = "@opencode-sdk-dotnet/live-absent-plugin";

    [Test]
    [Timeout(60_000)]
    public async Task AwaitPluginActivationAsync_Should_Answer_204_Once_Activation_Settles(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var settled = await client.Plugins.AwaitPluginActivationAsync(cancellationToken: cancellationToken);

        await Assert.That(settled.Status).IsEqualTo(204);
        await Assert.That(settled.IsError).IsFalse();

        Console.WriteLine("plugins-live: await-activation status=" + Number(settled.Status));
    }

    [Test]
    [Timeout(60_000)]
    public async Task ListPluginsAsync_Should_Report_The_Expected_Inventory(CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        // plugin.list reads the inventory as it stands (handlers/plugin.ts:11-13) while check and
        // update settle activation first (:20, :60); a cold server lists nothing until the builtins
        // activate, so this test settles activation itself before it asserts the inventory.
        _ = await client.Plugins.AwaitPluginActivationAsync(cancellationToken: cancellationToken);

        var listed = await client.Plugins.ListPluginsAsync(cancellationToken: cancellationToken);

        await Assert.That(listed.Status).IsEqualTo(200);
        await Assert.That(listed.IsError).IsFalse();
        await Assert.That(listed.Plugins.Count).IsGreaterThan(0);

        // The plugin, not the ownership, is what the inventory shape follows: every owned server
        // carries the seeded entry, whichever build the fixture started, and an external endpoint
        // carries none.
        if (server.RpcPlugin is { } ownedPlugin)
        {
            await AssertOwnedInventoryAsync(listed.Plugins, ownedPlugin);
        }
        else
        {
            await Assert.That(server.IsExternal).IsTrue();
            await Assert.That(IdsWhere(
                listed.Plugins,
                static plugin => string.Equals(plugin.Id, TestRpcPlugin.Id, StringComparison.Ordinal))).IsEmpty();
        }

        Console.WriteLine(
            "plugins-live: mode=" + (server.IsExternal ? "external" : "owned") +
            " list status=" + Number(listed.Status) +
            " count=" + Number(listed.Plugins.Count) +
            " ids=" + string.Join(", ", listed.Plugins.Select(static plugin => plugin.Id ?? "<null>")));
    }

    [Test]
    [Timeout(60_000)]
    public async Task CheckPluginUpdatesAsync_Should_Answer_The_Inventory_When_No_Target_Is_Named(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var listed = await GetActivatedInventoryAsync(client, cancellationToken);
        await Assert.That(IdsWhere(listed, static plugin => plugin.Source is PluginSourcePackage)).IsEmpty();

        var inventory = await client.Plugins.CheckPluginUpdatesAsync(cancellationToken: cancellationToken);

        await Assert.That(inventory.Status).IsEqualTo(200);
        await Assert.That(inventory.IsError).IsFalse();
        await Assert.That(inventory.Check.Count).IsGreaterThan(0);

        Console.WriteLine(
            "plugins-live: check status=" + Number(inventory.Status) +
            " count=" + Number(inventory.Check.Count) +
            " sources=" + string.Join(
                ", ", inventory.Check.Select(static plugin => plugin.Source.Type).Distinct(StringComparer.Ordinal)));
    }

    [Test]
    [Timeout(60_000)]
    public async Task CheckPluginUpdatesAsync_Should_Refuse_A_Target_Outside_The_Inventory(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var listed = await GetActivatedInventoryAsync(client, cancellationToken);
        await Assert.That(IdsWhere(listed, static plugin => plugin.Source is PluginSourcePackage)).IsEmpty();
        await Assert.That(IdsWhere(
            listed,
            static plugin => string.Equals(plugin.Id, AbsentTarget, StringComparison.Ordinal))).IsEmpty();

        var refused = await client.Plugins.CheckPluginUpdatesAsync(
            new PluginCheckPostRequest { Target = AbsentTarget }, OpenCodeRequestOptions.NoThrow, cancellationToken);

        await Assert.That(refused.Status).IsEqualTo(400);
        await Assert.That(refused.IsError).IsTrue();
        await Assert.That(refused.Error).IsTypeOf<InvalidRequestError>();
        var invalid = refused.Error as InvalidRequestError;
        await Assert.That(invalid?.Field).IsEqualTo("target");
        await Assert.That(invalid?.Message).Contains(AbsentTarget);

        Console.WriteLine(
            "plugins-live: check-unknown status=" + Number(refused.Status) +
            " field=" + invalid?.Field +
            " body=" + refused.RawBody);
    }

    [Test]
    [Timeout(60_000)]
    public async Task UpdatePluginsAsync_Should_Answer_204_When_No_Targets_Are_Named(CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var listed = await GetActivatedInventoryAsync(client, cancellationToken);
        await Assert.That(IdsWhere(listed, static plugin => plugin.Source is PluginSourcePackage)).IsEmpty();

        var updated = await client.Plugins.UpdatePluginsAsync(
            new PluginUpdatePostRequest { Targets = [] }, cancellationToken: cancellationToken);

        await Assert.That(updated.Status).IsEqualTo(204);
        await Assert.That(updated.IsError).IsFalse();

        Console.WriteLine("plugins-live: update-empty status=" + Number(updated.Status));
    }

    [Test]
    [Timeout(60_000)]
    public async Task UpdatePluginsAsync_Should_Refuse_A_Target_Outside_The_Inventory(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var listed = await GetActivatedInventoryAsync(client, cancellationToken);
        await Assert.That(IdsWhere(listed, static plugin => plugin.Source is PluginSourcePackage)).IsEmpty();
        await Assert.That(IdsWhere(
            listed,
            static plugin => string.Equals(plugin.Id, AbsentTarget, StringComparison.Ordinal))).IsEmpty();

        var refused = await client.Plugins.UpdatePluginsAsync(
            new PluginUpdatePostRequest { Targets = [AbsentTarget] }, OpenCodeRequestOptions.NoThrow, cancellationToken);

        await Assert.That(refused.Status).IsEqualTo(400);
        await Assert.That(refused.IsError).IsTrue();
        await Assert.That(refused.Error).IsTypeOf<InvalidRequestError>();
        var invalid = refused.Error as InvalidRequestError;
        await Assert.That(invalid?.Field).IsEqualTo("targets");
        await Assert.That(invalid?.Message).Contains(AbsentTarget);

        Console.WriteLine(
            "plugins-live: update-unknown status=" + Number(refused.Status) +
            " field=" + invalid?.Field +
            " body=" + refused.RawBody);
    }

    /// <summary>The ids of the inventory entries a predicate selects; empty is the passing answer.</summary>
    private static string[] IdsWhere(IReadOnlyList<PluginInfo> plugins, Func<PluginInfo, bool> predicate) =>
        [.. plugins.Where(predicate).Select(static plugin => plugin.Id ?? "<null>")];

    private static async Task<IReadOnlyList<PluginInfo>> GetActivatedInventoryAsync(
        OpenCodeClient client,
        CancellationToken cancellationToken)
    {
        _ = await client.Plugins.AwaitPluginActivationAsync(cancellationToken: cancellationToken);
        var listed = await client.Plugins.ListPluginsAsync(cancellationToken: cancellationToken);
        await Assert.That(listed.Status).IsEqualTo(200);
        await Assert.That(listed.IsError).IsFalse();
        return listed.Plugins;
    }

    private static async Task AssertOwnedInventoryAsync(
        IReadOnlyList<PluginInfo> plugins,
        TestRpcPlugin ownedPlugin)
    {
        await Assert.That(plugins.Select(static plugin => plugin.Id).ToArray()).Contains("opencode.agent");
        await Assert.That(IdsWhere(
            plugins,
            static plugin => plugin.Source is not PluginSourceBuiltin &&
                !string.Equals(plugin.Id, TestRpcPlugin.Id, StringComparison.Ordinal))).IsEmpty();

        var ownedEntries = plugins.Where(
            static plugin => string.Equals(plugin.Id, TestRpcPlugin.Id, StringComparison.Ordinal)).ToArray();
        await Assert.That(ownedEntries).Count().IsEqualTo(1);
        var owned = ownedEntries.Single();
        await Assert.That(owned.Source).IsTypeOf<PluginSourceLocal>();
        var local = owned.Source as PluginSourceLocal;
        await Assert.That(local?.Path).IsEqualTo(ownedPlugin.EntryPoint);
        await Assert.That(owned.State).IsTypeOf<PluginStateActive>();
    }

    /// <summary>Renders one number for the console line, culture-free.</summary>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
