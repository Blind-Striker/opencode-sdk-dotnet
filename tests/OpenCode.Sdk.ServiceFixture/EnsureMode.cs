using System.Diagnostics;
using System.Globalization;

namespace OpenCode.Sdk.ServiceFixture;

/// <summary>
/// Runs <see cref="OpenCodeServer.EnsureAsync"/> for one channel from a process whose environment
/// the launching test owns, and prints <c>ensured owns=&lt;true|false&gt; pid=&lt;pid&gt;
/// endpoint=&lt;url&gt;</c>. The default command <c>opencode serve --service</c> resolves through
/// this process's own PATH, which the test points at the forwarding shim, so the shipped executable
/// resolution is what starts the source-run daemon; the spawned service registers under the channel
/// this process's environment roots.
/// </summary>
internal static class EnsureMode
{
    public static async Task<int> RunAsync(string channel)
    {
        try
        {
            // The elapsed time goes to stderr, never to the stdout contract, the way the discovery
            // mode keeps its own bound out of the answer line.
            var stopwatch = Stopwatch.StartNew();
            var server = await OpenCodeServer.EnsureAsync(
                new OpenCodeServerEnsureOptions { Channel = channel }).ConfigureAwait(false);
            await Console.Error
                .WriteLineAsync($"ensure took {stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms")
                .ConfigureAwait(false);

            await using (server.ConfigureAwait(false))
            {
                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"ensured owns={(server.OwnsProcess ? "true" : "false")} pid={server.ProcessId} endpoint={server.Endpoint}");
                await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or OpenCodeServerException)
        {
            await Console.Error.WriteLineAsync(exception.GetType().Name + ": " + exception.Message).ConfigureAwait(false);
            return 1;
        }
    }
}
