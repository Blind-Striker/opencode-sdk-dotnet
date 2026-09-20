using System.Diagnostics;
using System.Globalization;

namespace OpenCode.Sdk.ServiceFixture;

/// <summary>
/// Runs <see cref="OpenCodeServer.DiscoverAsync"/> once and prints
/// <c>found owns=&lt;true|false&gt; pid=&lt;pid&gt; endpoint=&lt;url&gt;</c> or <c>missing</c>.
/// </summary>
internal static class DiscoveryMode
{
    public static async Task<int> RunAsync(OpenCodeServerDiscoverOptions? options)
    {
        try
        {
            // The elapsed time goes to stderr, never to the stdout contract: a "missing" that took
            // the whole request bound is a timed-out probe, one that took milliseconds is a
            // registration the process never found, and a test reading both can tell them apart.
            var stopwatch = Stopwatch.StartNew();
            var server = await OpenCodeServer.DiscoverAsync(options).ConfigureAwait(false);
            await Console.Error
                .WriteLineAsync($"discovery took {stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms")
                .ConfigureAwait(false);
            if (server is null)
            {
                await Console.Out.WriteLineAsync("missing").ConfigureAwait(false);
                return 0;
            }

            await using (server.ConfigureAwait(false))
            {
                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"found owns={(server.OwnsProcess ? "true" : "false")} pid={server.ProcessId} endpoint={server.Endpoint}");
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
