namespace OpenCode.Sdk.ServiceFixture;

/// <summary>
/// The isolated background-service entry point: runs one SDK door in a process whose environment
/// the launching test owns entirely, and reports the outcome on one stdout line. Split out of
/// <c>Program.cs</c>'s top-level statements to keep the dispatch under the repository's method-size
/// gates, the way the sandbox does; each mode's body lives in its own class.
/// </summary>
/// <remarks>
/// Modes: <c>discover-default</c> reads the shared release registration through every default;
/// <c>discover-channel &lt;channel&gt;</c> names a service channel; <c>stop-channel &lt;channel&gt;</c>
/// stops the channel's registered service; <c>idle</c> prints <c>ready</c> and lingers until it is
/// ended; <c>ignore-sigterm</c> lingers the same way but ignores <c>SIGTERM</c> where the platform
/// can deliver one. The credential is never printed. Exit 0 carries an answer, 1 a failure, 2 a
/// usage error.
/// </remarks>
internal static class ServiceFixtureRunner
{
    public static Task<int> RunAsync(string[] args) =>
        args switch
        {
            ["discover-default"] => DiscoveryMode.RunAsync(options: null),
            ["discover-channel", var channel] => DiscoveryMode.RunAsync(new OpenCodeServerDiscoverOptions { Channel = channel }),
            ["stop-channel", var channel] => StopMode.RunAsync(channel),
            ["idle"] => LingeringProcessMode.RunAsync(ignoreTerminate: false),
            ["ignore-sigterm"] => LingeringProcessMode.RunAsync(ignoreTerminate: true),
            _ => UsageAsync(),
        };

    private static async Task<int> UsageAsync()
    {
        await Console.Error
            .WriteLineAsync("Usage: discover-default | discover-channel <channel> | stop-channel <channel> | idle | ignore-sigterm")
            .ConfigureAwait(false);
        return 2;
    }
}
