using System.Globalization;
using OpenCode.Sdk.Internal.BackgroundService;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// The shipped <see cref="ServiceProcessControl"/> against real processes of this machine — the
/// isolated fixture's lingering modes, owned by each test: a stable identity for a running
/// process, none for one that left, a stale identity that is never signalled, and both rungs
/// ending what they are meant to end. Every test starts a process, hence the server-process key.
/// </summary>
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class ServiceProcessControlTests
{
    private static readonly RealFileSystem FileSystem = new();
    private static readonly TimeSpan ExitBound = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SurvivalBound = TimeSpan.FromSeconds(2);

    [Test]
    [Timeout(60_000)]
    public async Task TrySnapshot_Should_Identify_A_Running_Process_Stably(CancellationToken cancellationToken)
    {
        await using var lingering = await ServiceFixtureProcess.StartAsync(FileSystem, "idle", cancellationToken);
        var control = new ServiceProcessControl();

        var first = control.TrySnapshot(lingering.ProcessId);
        var second = control.TrySnapshot(lingering.ProcessId);

        await Assert.That(first).IsNotNull();
        await Assert.That(first!.Value.ProcessId).IsEqualTo(lingering.ProcessId);
        await Assert.That(first.Value.StartTimeUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    [Timeout(60_000)]
    public async Task TrySnapshot_Should_Return_Null_Once_The_Process_Is_Gone(CancellationToken cancellationToken)
    {
        await using var lingering = await ServiceFixtureProcess.StartAsync(FileSystem, "idle", cancellationToken);
        var processId = lingering.ProcessId;
        var control = new ServiceProcessControl();
        await lingering.DisposeAsync();

        var afterwards = await WaitForTheTableAsync(control, processId, cancellationToken);

        await Assert.That(afterwards).IsNull();
    }

    [Test]
    [Timeout(60_000)]
    public async Task TrySignal_Should_Refuse_A_Stale_Identity_And_Leave_The_Process_Running(CancellationToken cancellationToken)
    {
        await using var lingering = await ServiceFixtureProcess.StartAsync(FileSystem, "idle", cancellationToken);
        var control = new ServiceProcessControl();
        var live = control.TrySnapshot(lingering.ProcessId)!.Value;
        var stale = live with { StartTimeUtc = live.StartTimeUtc.AddHours(-1) };

        var sent = control.TrySignal(stale, ProcessSignal.Kill);

        await Assert.That(sent).IsFalse();
        await Assert.That(await lingering.ObserveExitWithinAsync(SurvivalBound, cancellationToken)).IsFalse();
        await Assert.That(lingering.HasExited).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task TrySignal_Should_End_A_Process_With_The_Terminate_Rung(CancellationToken cancellationToken)
    {
        await using var lingering = await ServiceFixtureProcess.StartAsync(FileSystem, "idle", cancellationToken);
        var control = new ServiceProcessControl();
        var live = control.TrySnapshot(lingering.ProcessId)!.Value;

        var sent = control.TrySignal(live, ProcessSignal.Terminate);

        await Assert.That(sent).IsTrue();
        await Assert.That(await lingering.ObserveExitWithinAsync(ExitBound, cancellationToken)).IsTrue();
        await Assert.That(await WaitForTheTableAsync(control, lingering.ProcessId, cancellationToken)).IsNull();
    }

    [Test]
    [Timeout(60_000)]
    public async Task TrySignal_Should_Reach_The_Kill_Rung_For_A_Process_That_Ignores_Terminate(CancellationToken cancellationToken)
    {
        await using var lingering = await ServiceFixtureProcess.StartAsync(FileSystem, "ignore-sigterm", cancellationToken);
        var control = new ServiceProcessControl();
        var live = control.TrySnapshot(lingering.ProcessId)!.Value;

        await Assert.That(control.TrySignal(live, ProcessSignal.Terminate)).IsTrue();
        var leftOnTheFirstRung = await lingering.ObserveExitWithinAsync(SurvivalBound, cancellationToken);

        if (OperatingSystem.IsWindows())
        {
            // Windows has no signal to ignore: the first rung is already TerminateProcess.
            Console.WriteLine("branch: Windows — the terminate rung is a hard kill and ended pid " + lingering.ProcessId.ToString(CultureInfo.InvariantCulture));
            await Assert.That(leftOnTheFirstRung).IsTrue();
            return;
        }

        Console.WriteLine("branch: Unix — pid " + lingering.ProcessId.ToString(CultureInfo.InvariantCulture) + " ignored SIGTERM, SIGKILL ends it");
        await Assert.That(leftOnTheFirstRung).IsFalse();
        await Assert.That(control.TrySnapshot(lingering.ProcessId)).IsEqualTo(live);
        await Assert.That(control.TrySignal(live, ProcessSignal.Kill)).IsTrue();
        await Assert.That(await lingering.ObserveExitWithinAsync(ExitBound, cancellationToken)).IsTrue();
        await Assert.That(await WaitForTheTableAsync(control, lingering.ProcessId, cancellationToken)).IsNull();
    }

    /// <summary>
    /// The exit is reported before the kernel finishes tearing the process down, so the pid can
    /// stay in the process table for a moment; its identity becomes unreadable soon after.
    /// </summary>
    private static Task<ProcessIdentity?> WaitForTheTableAsync(ServiceProcessControl control, int processId, CancellationToken cancellationToken) =>
        LiveReadiness.WaitAsync(
            _ => Task.FromResult(control.TrySnapshot(processId)),
            static identity => identity is null,
            "the ended process to leave the process table",
            cancellationToken);
}
