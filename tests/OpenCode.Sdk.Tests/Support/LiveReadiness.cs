using System.Diagnostics;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

internal static class LiveReadiness
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static async Task<T> WaitAsync<T>(
        Func<CancellationToken, Task<T>> observe,
        Func<T, bool> ready,
        string description,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Budget);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var observed = await observe(deadline.Token);
                if (ready(observed))
                {
                    return observed;
                }

                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for {description} after {elapsed.Elapsed}.", exception);
        }
    }

    public static Task<PluginListResponse> PluginAsync(
        OpenCodeClient client, string id, CancellationToken cancellationToken) =>
        WaitAsync(
            token => client.Plugins.ListPluginsAsync(cancellationToken: token),
            response => IsActive(response.Plugins, id),
            "active plugin " + id,
            cancellationToken);

    public static Task<VcsResponse> GitAsync(
        OpenCodeClient client, LocationSelector location, CancellationToken cancellationToken) =>
        WaitAsync(
            token => client.Vcs.GetVcsAsync(new VcsRequest { Location = location }, cancellationToken: token),
            response => response.Vcs.Provider == "git",
            "Git provider at " + location.Directory,
            cancellationToken);

    private static bool IsActive(IReadOnlyList<PluginInfo> plugins, string id)
    {
        var plugin = plugins.SingleOrDefault(candidate => candidate.Id == id);
        if (plugin?.State is PluginStateFailed failed)
        {
            throw new InvalidOperationException("Plugin " + id + " failed: " + failed.Error);
        }

        return plugin?.State is PluginStateActive;
    }
}
