namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// One Ensure election iteration as a pure record: the inputs the loop has already read, and
/// <see cref="Decide"/> the declared outcome. No I/O, no clock reads. The version filter is
/// already resolved as <see cref="VersionMatches"/>.
/// </summary>
/// <param name="Now">The wall clock at the start of this iteration.</param>
/// <param name="Deadline">The <c>PromiseTimeout</c> deadline; <paramref name="Now"/> at or past it throws.</param>
/// <param name="Registration">The registration file's contents, or null when absent or unusable.</param>
/// <param name="Probe">The info probe's answer for this registration; ignored when there is none.</param>
/// <param name="Timeouts">The consecutive-timeout streak, or null when none is in flight.</param>
/// <param name="Contenders">The live-and-finished contenders as observations, in list order.</param>
/// <param name="LastSpawn">When a contender was last spawned, or null for upstream's <c>0</c> sentinel.</param>
/// <param name="SpawnDelay">The current spawn delay, which exit-0 doubles up to the cap.</param>
/// <param name="Timing">The injected timing; spawn delay reset and cap come from here.</param>
/// <param name="VersionMatches">The already-resolved version predicate for this probe's version.</param>
internal sealed record ServiceElectionIteration(
    DateTimeOffset Now,
    DateTimeOffset Deadline,
    ServiceRegistration? Registration,
    ServiceProbeResult Probe,
    ServiceTimeoutCounter? Timeouts,
    IReadOnlyList<ServiceContenderObservation> Contenders,
    DateTimeOffset? LastSpawn,
    TimeSpan SpawnDelay,
    ServiceTiming Timing,
    bool VersionMatches)
{
    /// <summary>Declares this iteration's outcome from the supplied inputs alone.</summary>
    /// <returns>Return, throw, or continue with side-effect requests.</returns>
    public ServiceElectionDecision Decide()
    {
        if (Now >= Deadline)
        {
            return new ServiceElectionDecision.ThrowTimeout();
        }

        var effects = new List<ServiceElectionEffect>();
        var timeouts = Timeouts;
        var lastSpawn = LastSpawn;
        AdvanceTimeouts(effects, ref timeouts, ref lastSpawn);

        if (Probe.IsService && Registration is { } registration)
        {
            return DecideRegistered(registration, effects, timeouts, lastSpawn);
        }

        return DecideServiceLess(effects, timeouts, lastSpawn, SpawnDelay);
    }

    /// <summary>
    /// Consecutive timeouts on the same identity: at 3, announce missing, clear the sidecar,
    /// terminate, and set lastSpawn so the service-less branch can spawn immediately.
    /// </summary>
    private void AdvanceTimeouts(
        List<ServiceElectionEffect> effects,
        ref ServiceTimeoutCounter? timeouts,
        ref DateTimeOffset? lastSpawn)
    {
        if (Probe.TimedOut && Registration is { } timedOut)
        {
            var identity = ServiceRegistrationIdentity.Of(timedOut);
            var count = timeouts is not null && timeouts.Identity == identity ? timeouts.Count + 1 : 1;
            timeouts = new ServiceTimeoutCounter(identity, count);
            if (count >= 3)
            {
                effects.Add(new ServiceElectionEffect.AnnounceMissing());
                effects.Add(new ServiceElectionEffect.ClearHandoff());
                effects.Add(new ServiceElectionEffect.Terminate(timedOut));
                timeouts = null;
                lastSpawn = Now - SpawnDelay;
            }

            return;
        }

        timeouts = null;
    }

    /// <summary>
    /// A usable service is registered: reset the spawn delay, return when ready, throw when
    /// failed, replace when incompatible, and otherwise wait.
    /// </summary>
    private ServiceElectionDecision DecideRegistered(
        ServiceRegistration registration,
        List<ServiceElectionEffect> effects,
        ServiceTimeoutCounter? timeouts,
        DateTimeOffset? lastSpawn)
    {
        var spawnDelay = Timing.SpawnDelay;
        var compatible = Probe.Compatible && VersionMatches;
        if (compatible && Probe.State == ServiceState.Ready)
        {
            return new ServiceElectionDecision.ReturnReady(registration);
        }

        if (compatible && Probe.State == ServiceState.Failed)
        {
            return new ServiceElectionDecision.ThrowFailed();
        }

        if (!compatible)
        {
            effects.Add(new ServiceElectionEffect.AnnounceVersionMismatch(Probe.Version));
            effects.Add(new ServiceElectionEffect.ReplaceIncompatible(
                registration, PrepareHandoff: Probe.State == ServiceState.Ready));
            lastSpawn = null;
        }

        return FrozenContinue(timeouts, lastSpawn, spawnDelay, effects, harvested: []);
    }

    /// <summary>
    /// No usable service: harvest finished contenders, fail when a failure is seen and none are
    /// live, and spawn when fewer than two are live and the delay has elapsed.
    /// </summary>
    private ServiceElectionDecision DecideServiceLess(
        List<ServiceElectionEffect> effects,
        ServiceTimeoutCounter? timeouts,
        DateTimeOffset? lastSpawn,
        TimeSpan spawnDelay)
    {
        if (lastSpawn is null && Registration is not null)
        {
            lastSpawn = Now;
        }

        var harvested = new List<int>();
        var liveCount = 0;
        OpenCodeServerException? failure = null;
        var sawExitZero = false;
        for (var index = 0; index < Contenders.Count; index++)
        {
            var contender = Contenders[index];
            if (!contender.Finished)
            {
                liveCount++;
                continue;
            }

            harvested.Add(index);
            if (contender.ExitedZero)
            {
                sawExitZero = true;
            }

            failure ??= contender.Failure;
        }

        if (sawExitZero)
        {
            var doubled = spawnDelay + spawnDelay;
            spawnDelay = doubled <= Timing.MaxSpawnDelay ? doubled : Timing.MaxSpawnDelay;
        }

        if (failure is not null && liveCount == 0)
        {
            return new ServiceElectionDecision.ThrowContenderFailure(failure, harvested);
        }

        if (liveCount < 2 && SpawnDelayElapsed(lastSpawn, spawnDelay))
        {
            effects.Add(new ServiceElectionEffect.AnnounceMissing());
            effects.Add(new ServiceElectionEffect.Spawn());
            lastSpawn = Now;
        }

        return FrozenContinue(timeouts, lastSpawn, spawnDelay, effects, harvested);
    }

    private bool SpawnDelayElapsed(DateTimeOffset? lastSpawn, TimeSpan spawnDelay) =>
        lastSpawn is not { } spawned || Now - spawned >= spawnDelay;

    private static ServiceElectionDecision.Continue FrozenContinue(
        ServiceTimeoutCounter? timeouts,
        DateTimeOffset? lastSpawn,
        TimeSpan spawnDelay,
        List<ServiceElectionEffect> effects,
        IReadOnlyList<int> harvested) =>
        new(timeouts, lastSpawn, spawnDelay, [.. effects], harvested);
}
