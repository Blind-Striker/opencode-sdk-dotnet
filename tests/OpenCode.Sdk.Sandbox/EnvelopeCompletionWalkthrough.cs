namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// The envelope-completion leg of the standing walkthrough: the ref-to-array vcs branches call,
/// the directory location sibling, the session-active dictionary
/// payload, the info body /api/info answers, and the session-scoped context
/// read on the bound SessionClient.
/// </summary>
internal static class EnvelopeCompletionWalkthrough
{
    public static async Task RunAsync(OpenCodeClient client, SessionClient handle)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(handle);

        var branches = await client.Vcs.ListBranchesAsync().ConfigureAwait(false);

        Console.WriteLine($"vcs-branches: status={branches.Status} branches={branches.Branches.Count} location={branches.Location.Directory}");

        // An omitted directory resolves the server default.
        var location = await client.GetLocationAsync().ConfigureAwait(false);

        Console.WriteLine($"location: status={location.Status} directory={location.ResolvedLocation.Directory} project={location.ResolvedLocation.Project.Id}");

        var active = await client.Sessions.GetActiveAsync().ConfigureAwait(false);

        Console.WriteLine($"session-active: status={active.Status} active={active.Active.Count}");

        var server = await client.Server.GetInfoAsync().ConfigureAwait(false);

        var firstUrl = server.ServerInfo.Urls.Count > 0 ? server.ServerInfo.Urls[0] : "<none>";

        Console.WriteLine($"server: status={server.Status} urls={server.ServerInfo.Urls.Count} first={firstUrl}");

        var context = await handle.GetContextAsync().ConfigureAwait(false);

        Console.WriteLine($"session-context: status={context.Status} messages={context.Context.Count}");
    }
}
