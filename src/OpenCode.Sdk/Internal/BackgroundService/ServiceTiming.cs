namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The pinned client's lifecycle timing (<c>defaultEnsureTiming</c> in <c>service-timing.ts</c>),
/// injectable and never public: discovery uses the request bound, Ensure and Stop the rest, and
/// the tests accelerate all of them the way upstream's own fixture does.
/// </summary>
/// <param name="RequestTimeout">The bound on one status request.</param>
/// <param name="PollInterval">The Ensure loop's spacing between iterations.</param>
/// <param name="Attempts">The Ensure loop's recurrence count after the initial run.</param>
/// <param name="SpawnDelay">The delay before a contender is started while a registration is unresolved.</param>
/// <param name="MaxSpawnDelay">The cap on the exit-0 backoff.</param>
/// <param name="StopPollInterval">The spacing between liveness polls after a stop signal.</param>
/// <param name="StopPollAttempts">The liveness polls per termination rung.</param>
internal sealed record ServiceTiming(
    TimeSpan RequestTimeout,
    TimeSpan PollInterval,
    int Attempts,
    TimeSpan SpawnDelay,
    TimeSpan MaxSpawnDelay,
    TimeSpan StopPollInterval,
    int StopPollAttempts)
{
    /// <summary>Gets upstream's values at the pin: 2 s, 100 ms, 1200, 5 s, 30 s, 50 ms, 100.</summary>
    public static ServiceTiming Default { get; } = new(
        RequestTimeout: TimeSpan.FromSeconds(2),
        PollInterval: TimeSpan.FromMilliseconds(100),
        Attempts: 1_200,
        SpawnDelay: TimeSpan.FromSeconds(5),
        MaxSpawnDelay: TimeSpan.FromSeconds(30),
        StopPollInterval: TimeSpan.FromMilliseconds(50),
        StopPollAttempts: 100);
}
