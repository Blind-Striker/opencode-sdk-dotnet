using System.Globalization;

namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// The launcher demo (<c>--standalone</c>): the SDK starts a private <c>opencode serve</c> through
/// <see cref="OpenCodeServer.StartAsync"/>, resolved from <c>PATH</c> the way a shell resolves it
/// (<c>PATHEXT</c> included on Windows, so an npm <c>.cmd</c> shim starts), calls health, runs the
/// model-selection recipe, and stops the server on dispose.
/// </summary>
internal static class StandaloneServerWalkthrough
{
    public static async Task<int> RunAsync()
    {
        await using var server = await OpenCodeServer.StartAsync().ConfigureAwait(false);
        Console.WriteLine(
            $"started: {server.Endpoint} (pid {server.ProcessId.ToString(CultureInfo.InvariantCulture)})");

        using var client = server.CreateClient();
        using var probeWindow = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var status = await client.Server.GetInfoAsync(cancellationToken: probeWindow.Token).ConfigureAwait(false);
        Console.WriteLine(
            $"status: {status.Status}, version: {status.ServerInfo.Version}, pid: {status.ServerInfo.Pid.ToString(CultureInfo.InvariantCulture)}");
        // The status call throws on anything but its declared success, so reaching this line is
        // the proof; it is also exactly the moment the model catalog can still be empty.
        await ModelSelectionWalkthrough.RunAsync(client).ConfigureAwait(false);
        return 0;
    }
}
