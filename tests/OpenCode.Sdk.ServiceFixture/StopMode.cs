using System.Diagnostics;
using System.Globalization;

namespace OpenCode.Sdk.ServiceFixture;

/// <summary>
/// Runs <see cref="OpenCodeServer.StopAsync"/> for one channel from a process whose environment the
/// launching test owns, and prints <c>stopped</c> when the call completed.
/// </summary>
internal static class StopMode
{
    public static async Task<int> RunAsync(string channel)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            await OpenCodeServer.StopAsync(new OpenCodeServerStopOptions { Channel = channel }).ConfigureAwait(false);
            await Console.Error
                .WriteLineAsync($"stop took {stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms")
                .ConfigureAwait(false);
            await Console.Out.WriteLineAsync("stopped").ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or OpenCodeServerException)
        {
            await Console.Error.WriteLineAsync(exception.GetType().Name + ": " + exception.Message).ConfigureAwait(false);
            return 1;
        }
    }
}
