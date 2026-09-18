using System.Globalization;

namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// The M4 demo leg: the SDK starts the server itself and calls health — no
/// OPENCODE_SANDBOX_ENDPOINT, no ambient server. OPENCODE_SANDBOX_SERVER_COMMAND ('|'-separated
/// to survive paths with spaces) overrides the command; unset uses the product default
/// (<c>opencode serve</c>, resolved from PATH the way a shell would — PATHEXT included on
/// Windows, so an npm .cmd shim starts). Door 2 (explicit endpoint) is the same tail without
/// StartAsync:
/// construct the client against a known endpoint and run the same bounded health probe.
/// </summary>
internal static class StandaloneServerWalkthrough
{
    public static async Task<int> RunAsync()
    {
        var options = new OpenCodeServerOptions();
        var commandVariable = Environment.GetEnvironmentVariable("OPENCODE_SANDBOX_SERVER_COMMAND");
        if (!string.IsNullOrWhiteSpace(commandVariable))
        {
            options.Command = commandVariable.Split('|', StringSplitOptions.RemoveEmptyEntries);
        }

        await using var server = await OpenCodeServer.StartAsync(options).ConfigureAwait(false);
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
