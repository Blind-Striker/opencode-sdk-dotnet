namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// The envelope-completion leg of the standing walkthrough: the ref-to-array vcs branches call,
/// the location sibling with its workspace-named query member, the session-active dictionary
/// payload, the flattened single-key body /api/server answers, and the session-scoped context
/// read on the bound SessionClient.
/// </summary>
internal static class EnvelopeCompletionWalkthrough
{
    public static async Task RunAsync(OpenCodeClient client, SessionClient handle)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(handle);

        var branches = await client.Vcs.GetBranchesAsync().ConfigureAwait(false);

        Console.WriteLine($"vcs-branches: status={branches.Status} branches={branches.Branches.Count} location={branches.Location.Directory}");

        // Left unset: the location query member rides the wire as location[workspace]
        // (LocationSelector.Workspace) — the body elsewhere names the same concept workspaceID
        // (LocationInfo.WorkspaceId) — so an unset request just resolves the server default.
        var location = await client.GetLocationAsync().ConfigureAwait(false);

        Console.WriteLine($"location: status={location.Status} directory={location.ResolvedLocation.Directory} project={location.ResolvedLocation.Project.Id}");

        var active = await client.Sessions.GetActiveAsync().ConfigureAwait(false);

        Console.WriteLine($"session-active: status={active.Status} active={active.Active.Count}");

        var server = await client.Server.GetServerAsync().ConfigureAwait(false);

        var firstUrl = server.Urls.Count > 0 ? server.Urls[0] : "<none>";

        Console.WriteLine($"server: status={server.Status} urls={server.Urls.Count} first={firstUrl}");

        var context = await handle.GetContextAsync().ConfigureAwait(false);

        Console.WriteLine($"session-context: status={context.Status} messages={context.Context.Count}");
    }
}
