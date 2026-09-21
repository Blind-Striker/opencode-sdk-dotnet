using OpenCode.Sdk.Internal.BackgroundService;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// The pure election iteration: one set of inputs, one declared outcome. No I/O, no clock reads.
/// </summary>
public sealed class ServiceElectionIterationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline = Now.AddSeconds(120);
    private static readonly ServiceTiming Timing = ServiceTiming.Default;
    private static readonly ServiceProbeResult NoService = new(State: null, Version: null, TimedOut: false);
    private static readonly OpenCodeServerException ContenderFailed =
        new("The background service contender (pid 1) exited with code 1.");

    [Test]
    public async Task Decide_Should_Throw_Timeout_When_The_Deadline_Has_Passed()
    {
        var decision = Iteration(now: Deadline).Decide();

        await Assert.That(decision).IsTypeOf<ServiceElectionDecision.ThrowTimeout>();
    }

    [Test]
    public async Task Decide_Should_Return_Ready_When_The_Service_Is_Compatible_And_Ready()
    {
        var registration = Modern();
        var decision = Iteration(
            registration,
            new ServiceProbeResult(ServiceState.Ready, "2.0.3", TimedOut: false),
            versionMatches: true).Decide();

        var ready = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.ReturnReady>();
        await Assert.That(ready!.Registration).IsSameReferenceAs(registration);
    }

    [Test]
    public async Task Decide_Should_Throw_Failed_When_The_Service_Is_Compatible_And_Failed()
    {
        var decision = Iteration(
            Modern(),
            new ServiceProbeResult(ServiceState.Failed, "2.0.3", TimedOut: false),
            versionMatches: true).Decide();

        await Assert.That(decision).IsTypeOf<ServiceElectionDecision.ThrowFailed>();
    }

    [Test]
    public async Task Decide_Should_Continue_When_The_Service_Is_Compatible_And_Waiting()
    {
        var decision = Iteration(
            Modern(),
            new ServiceProbeResult(ServiceState.Waiting, "2.0.3", TimedOut: false),
            versionMatches: true).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.Effects).IsEmpty();
        await Assert.That(next.SpawnDelay).IsEqualTo(Timing.SpawnDelay);
    }

    [Test]
    public async Task Decide_Should_Replace_When_The_Service_Is_Incompatible()
    {
        var registration = Modern();
        var decision = Iteration(
            registration,
            new ServiceProbeResult(ServiceState.Ready, "1.0.0", TimedOut: false) { Compatible = false },
            versionMatches: true).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.LastSpawn).IsNull();
        await Assert.That(next.Effects.Count).IsEqualTo(2);
        var announce = await Assert.That(next.Effects[0]).IsTypeOf<ServiceElectionEffect.AnnounceVersionMismatch>();
        await Assert.That(announce!.PreviousVersion).IsEqualTo("1.0.0");
        var replace = await Assert.That(next.Effects[1]).IsTypeOf<ServiceElectionEffect.ReplaceIncompatible>();
        await Assert.That(replace!.Registration).IsSameReferenceAs(registration);
        await Assert.That(replace.PrepareHandoff).IsTrue();
    }

    [Test]
    public async Task Decide_Should_Clear_Rather_Than_Handoff_When_The_Incompatible_Service_Is_Not_Ready()
    {
        var decision = Iteration(
            Modern(),
            new ServiceProbeResult(ServiceState.Waiting, "1.0.0", TimedOut: false),
            versionMatches: false).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        var replace = await Assert.That(next!.Effects[1]).IsTypeOf<ServiceElectionEffect.ReplaceIncompatible>();
        await Assert.That(replace!.PrepareHandoff).IsFalse();
    }

    [Test]
    public async Task Decide_Should_Terminate_After_Three_Timeouts_On_The_Same_Identity()
    {
        var registration = Modern();
        var identity = ServiceRegistrationIdentity.Of(registration);
        var decision = Iteration(
            registration,
            new ServiceProbeResult(State: null, Version: null, TimedOut: true),
            timeouts: new ServiceTimeoutCounter(identity, 2),
            lastSpawn: Now).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.Timeouts).IsNull();
        await Assert.That(next.Effects[0]).IsTypeOf<ServiceElectionEffect.AnnounceMissing>();
        await Assert.That(next.Effects[1]).IsTypeOf<ServiceElectionEffect.ClearHandoff>();
        var terminate = await Assert.That(next.Effects[2]).IsTypeOf<ServiceElectionEffect.Terminate>();
        await Assert.That(terminate!.Registration).IsSameReferenceAs(registration);
        await Assert.That(next.Effects[3]).IsTypeOf<ServiceElectionEffect.AnnounceMissing>();
        await Assert.That(next.Effects[4]).IsTypeOf<ServiceElectionEffect.Spawn>();
        await Assert.That(next.LastSpawn).IsEqualTo(Now);
    }

    [Test]
    public async Task Decide_Should_Count_The_First_Timeout_Without_Terminating()
    {
        var registration = Modern();
        var decision = Iteration(
            registration,
            new ServiceProbeResult(State: null, Version: null, TimedOut: true),
            lastSpawn: Now).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.Timeouts!.Count).IsEqualTo(1);
        await Assert.That(next.Timeouts.Identity).IsEqualTo(ServiceRegistrationIdentity.Of(registration));
        await Assert.That(next.Effects.Any(static effect => effect is ServiceElectionEffect.Terminate)).IsFalse();
    }

    [Test]
    public async Task Decide_Should_Spawn_Immediately_When_No_Registration_Is_Present()
    {
        var decision = Iteration(registration: null, NoService, lastSpawn: null).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.Effects[0]).IsTypeOf<ServiceElectionEffect.AnnounceMissing>();
        await Assert.That(next.Effects[1]).IsTypeOf<ServiceElectionEffect.Spawn>();
        await Assert.That(next.LastSpawn).IsEqualTo(Now);
    }

    [Test]
    public async Task Decide_Should_Wait_The_Spawn_Delay_When_A_Registration_Exists_But_Is_Not_A_Service()
    {
        var decision = Iteration(
            Modern(),
            NoService,
            lastSpawn: null).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.Effects).IsEmpty();
        await Assert.That(next.LastSpawn).IsEqualTo(Now);
    }

    [Test]
    public async Task Decide_Should_Harvest_Finished_Contenders_And_Cap_Live_Spawns_At_Two()
    {
        var live = new ServiceContenderObservation(Finished: false, ExitedZero: false, Failure: null);
        var decision = Iteration(
            registration: null,
            NoService,
            contenders: [live, live],
            lastSpawn: Now.AddSeconds(-10)).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.Effects).IsEmpty();
        await Assert.That(next.HarvestedIndices).IsEmpty();
    }

    [Test]
    public async Task Decide_Should_Double_Spawn_Delay_When_A_Finished_Contender_Exited_Zero()
    {
        var finishedZero = new ServiceContenderObservation(Finished: true, ExitedZero: true, Failure: null);
        var live = new ServiceContenderObservation(Finished: false, ExitedZero: false, Failure: null);
        var decision = Iteration(
            registration: null,
            NoService,
            contenders: [finishedZero, live],
            lastSpawn: Now,
            spawnDelay: TimeSpan.FromSeconds(5)).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.SpawnDelay).IsEqualTo(TimeSpan.FromSeconds(10));
        await Assert.That(next.HarvestedIndices.Count).IsEqualTo(1);
        await Assert.That(next.HarvestedIndices[0]).IsEqualTo(0);
        await Assert.That(next.Effects).IsEmpty();
    }

    [Test]
    public async Task Decide_Should_Cap_The_Doubled_Spawn_Delay()
    {
        var finishedZero = new ServiceContenderObservation(Finished: true, ExitedZero: true, Failure: null);
        var decision = Iteration(
            registration: null,
            NoService,
            contenders: [finishedZero],
            lastSpawn: Now,
            spawnDelay: TimeSpan.FromSeconds(20)).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.SpawnDelay).IsEqualTo(Timing.MaxSpawnDelay);
    }

    [Test]
    public async Task Decide_Should_Throw_When_A_Contender_Failed_And_None_Remain_Live()
    {
        var finished = new ServiceContenderObservation(Finished: true, ExitedZero: false, ContenderFailed);
        var decision = Iteration(
            registration: null,
            NoService,
            contenders: [finished]).Decide();

        var thrown = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.ThrowContenderFailure>();
        await Assert.That(thrown!.Failure).IsSameReferenceAs(ContenderFailed);
        await Assert.That(thrown.HarvestedIndices.Count).IsEqualTo(1);
        await Assert.That(thrown.HarvestedIndices[0]).IsEqualTo(0);
    }

    [Test]
    public async Task Decide_Should_Not_Throw_A_Contender_Failure_While_Another_Is_Live()
    {
        var finished = new ServiceContenderObservation(Finished: true, ExitedZero: false, ContenderFailed);
        var live = new ServiceContenderObservation(Finished: false, ExitedZero: false, Failure: null);
        var decision = Iteration(
            registration: null,
            NoService,
            contenders: [finished, live],
            lastSpawn: Now).Decide();

        var next = await Assert.That(decision).IsTypeOf<ServiceElectionDecision.Continue>();
        await Assert.That(next!.HarvestedIndices.Count).IsEqualTo(1);
        await Assert.That(next.HarvestedIndices[0]).IsEqualTo(0);
        await Assert.That(next.Effects.Any(static effect => effect is ServiceElectionEffect.Spawn)).IsFalse();
    }

    private static ServiceElectionIteration Iteration(
        ServiceRegistration? registration = null,
        ServiceProbeResult? probe = null,
        ServiceTimeoutCounter? timeouts = null,
        IReadOnlyList<ServiceContenderObservation>? contenders = null,
        DateTimeOffset? lastSpawn = null,
        TimeSpan? spawnDelay = null,
        DateTimeOffset? now = null,
        bool versionMatches = true) =>
        new(
            now ?? Now,
            Deadline,
            registration,
            probe ?? NoService,
            timeouts,
            contenders ?? [],
            lastSpawn,
            spawnDelay ?? Timing.SpawnDelay,
            Timing,
            versionMatches);

    private static ServiceRegistration Modern() =>
        new("srv_1", "2.0.3", "http://127.0.0.1:1", new Uri("http://127.0.0.1:1"), 48213, "pw");
}
