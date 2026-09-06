using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;
using OpenCode.Sdk.TestSupport.Ownership;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// Pins the failure-path log-retention contract: a real <see cref="PinnedOpenCodeServerFixture"/>
/// failures retain bounded stdout/stderr and identity beneath the results root, beyond the lower-level
/// adapter's own in-memory buffers. Drives the fixture itself (its internal command-override
/// seam), not the shared per-session instance the other fixture tests share.
/// </summary>
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class PinnedOpenCodeServerFixtureFailureTests
{

    [Test]
    [Timeout(30_000)]
    public async Task DisposeAsync_Should_Retain_A_Late_Capture_Failure_Without_Delaying_Other_Artifacts(CancellationToken cancellationToken)
    {
        using var barrier = new DiagnosticWriteBarrier();
        var captureFailure = new IOException("Late stderr write failure.");
        var scenario = new ServerDiagnosticScenario { CaptureBarrier = barrier, CaptureFailure = captureFailure };
        var deadline = scenario.Deadlines.Hold("pinned server stderr capture");
        await using var fixture = scenario.CreateFixture();
        var primary = new InvalidOperationException("Deliberate body failure before late capture.");
        var completion = Task.CompletedTask;
        try
        {
            await fixture.InitializeAsync();
            fixture.MarkFailure(primary, "late capture body", "pty-phase=initial READY");
            completion = fixture.DisposeAsync().AsTask();
            await barrier.Entered.WaitAsync(cancellationToken);
            await deadline.Entered.WaitAsync(cancellationToken);
            deadline.Release();

            var observed = await Assert.That(async () => await completion).Throws<InvalidOperationException>();
            await Assert.That(observed).IsSameReferenceAs(primary);
            await Assert.That(await scenario.ReadLogAsync(fixture, "stdout.log")).Contains("FINAL-STDOUT");
            await Assert.That(await scenario.ReadMetadataAsync(fixture)).Contains("pinned server stderr capture");
            barrier.Release();
            await scenario.Deadlines.DrainAsync(completion);
            await fixture.DrainDiagnosticsAsync(cancellationToken);
            var late = primary.Data[OwnedCleanup.LateFailuresKey]
                as System.Collections.Concurrent.ConcurrentQueue<KeyValuePair<string, Exception>>;
            await Assert.That(late!.Single().Value).IsSameReferenceAs(captureFailure);
        }
        finally
        {
            barrier.Release();
            await scenario.DisposeFixtureAsync(fixture, completion);
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task DisposeAsync_Should_Report_An_Unconfirmed_Forced_Exit_Without_A_Body_Failure(CancellationToken cancellationToken)
    {
        var scenario = new ServerDiagnosticScenario();
        var grace = scenario.Deadlines.Hold("pinned server graceful exit");
        var forced = scenario.Deadlines.Hold("pinned server forced exit");
        await using var fixture = scenario.CreateFixture();
        var completion = Task.CompletedTask;
        try
        {
            await fixture.InitializeAsync();
            completion = fixture.DisposeAsync().AsTask();
            await grace.Entered.WaitAsync(cancellationToken);
            _ = await fixture.Adapter.WaitForErrorLineAsync("FINAL-STDERR", cancellationToken);
            grace.Release();
            await forced.Entered.WaitAsync(cancellationToken);
            forced.Release();

            var observed = await Assert.That(async () => await completion).Throws<TimeoutException>();
            await Assert.That(observed!.Message).Contains("pinned server forced exit");
            await Assert.That(observed.InnerException).IsSameReferenceAs(forced.Failure);
            await fixture.DrainDiagnosticsAsync(cancellationToken);
            await Assert.That(await scenario.ReadLogAsync(fixture, "stderr.log")).Contains("FINAL-STDERR");
            await Assert.That(await scenario.ReadMetadataAsync(fixture)).Contains("pinned server forced exit");
        }
        finally
        {
            await scenario.DisposeFixtureAsync(fixture, completion);
        }
    }

    [Test]
    [Arguments(StartupDiagnosticMode.InvalidReadiness)]
    [Arguments(StartupDiagnosticMode.ReadinessTimeout)]
    [Arguments(StartupDiagnosticMode.CallerCancellation)]
    [Arguments(StartupDiagnosticMode.EarlyExit)]
    [Timeout(30_000)]
    public async Task StartAsync_Should_Preserve_Startup_Failure_When_Teardown_Also_Times_Out(
        StartupDiagnosticMode mode, CancellationToken cancellationToken)
    {
        var scenario = new ServerDiagnosticScenario();
        var deadline = scenario.Deadlines.Hold("pinned server startup teardown");
        using var caller = new CancellationTokenSource();
        if (mode is StartupDiagnosticMode.CallerCancellation)
        {
            await caller.CancelAsync();
        }

        CliWrapServerAdapter? adapter = null;
        var startup = scenario.StartAdapterAsync(mode, created => adapter = created, caller.Token);
        try
        {
            await deadline.Entered.WaitAsync(cancellationToken);
            var ownedAdapter = adapter ?? throw new InvalidOperationException("The startup adapter was not constructed.");
            _ = await ownedAdapter.WaitForErrorLineAsync("FINAL-STDERR", cancellationToken);
            deadline.Expire();
            await deadline.Won.WaitAsync(cancellationToken);
            deadline.Deliver();

            var failure = await Assert.That(async () => await startup).ThrowsException();
            if (mode is StartupDiagnosticMode.CallerCancellation)
            {
                await Assert.That(failure).IsTypeOf<OperationCanceledException>();
                await Assert.That(((OperationCanceledException)failure!).CancellationToken).IsEqualTo(caller.Token);
            }
            else
            {
                await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            }

            var secondary = failure!.Data[OwnedCleanup.FailuresKey] as AggregateException;
            await Assert.That(secondary!.InnerExceptions.OfType<TimeoutException>().Single().InnerException)
                .IsSameReferenceAs(deadline.Failure);
            await Assert.That(failure.Data["PinnedServer.StartupLogs"] as string).Contains("FINAL-STDERR");
        }
        finally
        {
            await scenario.Deadlines.DrainAsync(startup);
            if (adapter is not null)
            {
                await adapter.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task DisposeAsync_Should_Capture_Final_Output_And_Preserve_The_Body_Failure()
    {
        var scenario = new ServerDiagnosticScenario();
        var fixture = scenario.CreateFixture();
        var primary = new InvalidOperationException("Deliberate live body failure.");
        try
        {
            await fixture.InitializeAsync();
            fixture.MarkFailure(primary, "controlled body", "pty-phase=initial READY; pty-id=controlled-pty");

            var observed = await Assert.That(async () => await fixture.DisposeAsync()).Throws<InvalidOperationException>();

            await Assert.That(observed).IsSameReferenceAs(primary);
            await Assert.That(primary.Data[OwnedCleanup.FailuresKey]).IsNull();
            await Assert.That(await scenario.ReadLogAsync(fixture, "stdout.log")).Contains("FINAL-STDOUT");
            await Assert.That(await scenario.ReadLogAsync(fixture, "stderr.log")).Contains("FINAL-STDERR");
            var metadata = await scenario.ReadMetadataAsync(fixture);
            await Assert.That(metadata).Contains("controlled body");
            await Assert.That(metadata).Contains("framework=");
            await Assert.That(metadata).Contains("pty-phase=initial READY");
        }
        finally
        {
            await scenario.DisposeFixtureAsync(fixture, Task.CompletedTask);
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task DisposeAsync_Should_Preserve_Teardown_And_Capture_Failures_With_Available_Output(CancellationToken cancellationToken)
    {
        var captureFailure = new IOException("Deliberate stderr capture failure.");
        var scenario = new ServerDiagnosticScenario { CaptureFailure = captureFailure };
        var deadline = scenario.Deadlines.Hold("pinned server teardown");
        var fixture = scenario.CreateFixture();
        var primary = new InvalidOperationException("Deliberate live body failure.");
        var completion = Task.CompletedTask;
        try
        {
            await fixture.InitializeAsync();
            fixture.MarkFailure(primary, "controlled teardown", "pty-phase=initial READY");
            completion = fixture.DisposeAsync().AsTask();
            await deadline.Entered.WaitAsync(cancellationToken);
            _ = await fixture.Adapter.WaitForErrorLineAsync("FINAL-STDERR", cancellationToken);
            deadline.Expire();
            await deadline.Won.WaitAsync(cancellationToken);
            deadline.Deliver();

            var observed = await Assert.That(async () => await completion).Throws<InvalidOperationException>();
            await Assert.That(observed).IsSameReferenceAs(primary);
            var failures = primary.Data[OwnedCleanup.FailuresKey] as AggregateException;
            await Assert.That(failures!.InnerExceptions).Contains(captureFailure);
            await Assert.That(failures.InnerExceptions.OfType<TimeoutException>().Any()).IsTrue();
            await Assert.That(await scenario.ReadLogAsync(fixture, "stdout.log")).Contains("FINAL-STDOUT");
            await Assert.That(await scenario.ReadMetadataAsync(fixture)).Contains("Deliberate live body failure");
        }
        finally
        {
            await scenario.DisposeFixtureAsync(fixture, completion);
        }
    }

    [Test]
    public async Task InitializeAsync_Should_Retain_Logs_Under_The_Results_Root_On_Failure()
    {
        var scenario = new ServerDiagnosticScenario();
        await using var fixture = scenario.CreateFixture("Server.exited-diagnostic-peer.js");
        try
        {
            var startup = await Assert.That(fixture.InitializeAsync).Throws<InvalidOperationException>();
            var disposal = await Assert.That(async () => await fixture.DisposeAsync()).Throws<InvalidOperationException>();
            await Assert.That(disposal).IsSameReferenceAs(startup);
            await Assert.That(await scenario.ReadLogAsync(fixture, "stderr.log")).Contains("FINAL-STDERR");
            await Assert.That(await scenario.ReadMetadataAsync(fixture)).Contains("phase=server startup");
        }
        finally
        {
            await scenario.DisposeFixtureAsync(fixture, Task.CompletedTask);
        }
    }
}
