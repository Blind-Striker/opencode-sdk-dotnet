namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// What one election iteration declares: return a ready service, throw, or continue after the
/// requested side effects.
/// </summary>
internal abstract record ServiceElectionDecision
{
    private ServiceElectionDecision()
    {
    }

    /// <summary>A compatible ready service won; complete the handoff and return it.</summary>
    /// <param name="Registration">The winning registration.</param>
    internal sealed record ReturnReady(ServiceRegistration Registration) : ServiceElectionDecision;

    /// <summary>The wall-clock deadline has passed.</summary>
    internal sealed record ThrowTimeout : ServiceElectionDecision;

    /// <summary>A compatible service reported the failed state.</summary>
    internal sealed record ThrowFailed : ServiceElectionDecision;

    /// <summary>A finished contender failed and none remain live.</summary>
    /// <param name="Failure">The contender failure to throw.</param>
    /// <param name="HarvestedIndices">Finished contenders to drop before throwing, matching upstream's delete-then-throw.</param>
    internal sealed record ThrowContenderFailure(
        OpenCodeServerException Failure,
        IReadOnlyList<int> HarvestedIndices) : ServiceElectionDecision;

    /// <summary>Run the side effects, keep the loop state, delay, and iterate.</summary>
    /// <param name="Timeouts">The timeout counter to carry, or null when the streak broke.</param>
    /// <param name="LastSpawn">When a contender was last spawned, or null for the <c>lastSpawn === 0</c> sentinel.</param>
    /// <param name="SpawnDelay">The current spawn delay, possibly doubled after an exit-0.</param>
    /// <param name="Effects">Side effects to run in order before the delay.</param>
    /// <param name="HarvestedIndices">Finished contenders to drop from the live set.</param>
    internal sealed record Continue(
        ServiceTimeoutCounter? Timeouts,
        DateTimeOffset? LastSpawn,
        TimeSpan SpawnDelay,
        IReadOnlyList<ServiceElectionEffect> Effects,
        IReadOnlyList<int> HarvestedIndices) : ServiceElectionDecision;
}
