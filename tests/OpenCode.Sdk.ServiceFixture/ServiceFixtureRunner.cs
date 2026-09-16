using System.Globalization;

namespace OpenCode.Sdk.ServiceFixture;

/// <summary>
/// The isolated discovery entry point: runs <see cref="OpenCodeServer.DiscoverAsync"/> in a process
/// whose environment the launching test owns entirely, and reports the outcome on one stdout line.
/// Split out of <c>Program.cs</c>'s top-level statements to keep the dispatch under the repository's
/// method-size gates, the way the sandbox does.
/// </summary>
/// <remarks>
/// Modes: <c>discover-default</c> reads the shared release registration through every default;
/// <c>discover-channel &lt;channel&gt;</c> names a service channel. The line is
/// <c>found owns=&lt;true|false&gt; pid=&lt;pid&gt; endpoint=&lt;url&gt;</c> or <c>missing</c>;
/// the credential is never printed. Exit 0 carries either answer, 1 a discovery failure, 2 a usage
/// error.
/// </remarks>
internal static class ServiceFixtureRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        OpenCodeServerDiscoverOptions? options;
        switch (args)
        {
            case ["discover-default"]:
                options = null;
                break;
            case ["discover-channel", var channel]:
                options = new OpenCodeServerDiscoverOptions { Channel = channel };
                break;
            default:
                await Console.Error.WriteLineAsync("Usage: discover-default | discover-channel <channel>").ConfigureAwait(false);
                return 2;
        }

        try
        {
            var server = await OpenCodeServer.DiscoverAsync(options).ConfigureAwait(false);
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
