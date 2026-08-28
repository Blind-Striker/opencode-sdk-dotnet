using System.Globalization;

namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// The M4 demo leg: the SDK starts the server itself and calls health — no
/// OPENCODE_SANDBOX_ENDPOINT, no ambient server. OPENCODE_SANDBOX_SERVER_COMMAND ('|'-separated
/// to survive paths with spaces) overrides the command; unset uses the product default
/// (opencode serve from PATH). Door 2 (explicit endpoint) is the same tail without StartAsync:
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
        var health = await client.GetHealthAsync(cancellationToken: probeWindow.Token).ConfigureAwait(false);
        Console.WriteLine(
            $"healthy: {health.Health.Healthy}, version: {health.Health.Version}, pid: {health.Health.Pid.ToString(CultureInfo.InvariantCulture)}");
        return health.Health.Healthy ? 0 : 1;
    }
}
