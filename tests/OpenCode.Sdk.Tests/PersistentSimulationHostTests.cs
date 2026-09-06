using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class PersistentSimulationHostTests(SimulatedDriveServerFixture server)
{
    [Test]
    [Timeout(60_000)]
    public async Task Host_Should_Route_Diagnostics_Away_From_Readiness_Stdout(
        CancellationToken _)
    {
        var beforeReady = server.PreReadinessDiagnostic;
        var afterReady = server.PostReadinessDiagnostic;

        await Assert.That(beforeReady).Contains("level=INFO");
        await Assert.That(afterReady).Contains("level=WARN");
        Console.WriteLine("persistent-host-diagnostics: " + beforeReady + " | " + afterReady);
    }
}
