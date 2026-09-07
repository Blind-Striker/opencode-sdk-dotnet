using OpenCode.Sdk.Internal;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The persistent simulation host's diagnostic contract, proven over one dedicated real
/// lifecycle through the SDK's own launcher and collector: the readiness line is the only thing
/// on stdout, and the three lifecycle milestones reach stderr with their severity, in order,
/// the last one written only once the stdin lease is released. The shared
/// <see cref="SimulatedDriveServerFixture"/> rechecks only the stdin-EOF milestone at its own
/// teardown (the earlier two are evicted over a chatty session); this test is where the
/// profile's routing is proven on its own. No drive controller is attached here: the milestones
/// do not depend on the control bootstrap, and readiness already proves the manifest's backend
/// port is bound.
/// </summary>
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class PersistentSimulationHostTests
{
    private static readonly RealFileSystem FileSystem = new();

    [Test]
    [Timeout(240_000)]
    public async Task Host_Should_Route_Diagnostics_To_Stderr_Around_Readiness_And_Stdin_Eof(
        CancellationToken cancellationToken)
    {
        using var runRoot = new TestRunRoot(FileSystem);
        var output = new OpenCodeServerOutput();
        await using (var server = await StartHostAsync(runRoot, output, cancellationToken))
        {
            await Assert.That(server.Endpoint.IsLoopback).IsTrue();
        }

        var snapshot = output.GetSnapshot();
        await Assert.That(snapshot.StandardOutput.Count).IsEqualTo(1);
        await Assert.That(ServerReadyLine.TryParse(snapshot.StandardOutput[0], out _)).IsTrue();

        var starting = RequireIndex(snapshot, "persistent simulation host starting");
        var ready = RequireIndex(snapshot, "persistent simulation host ready");
        var closed = RequireIndex(snapshot, "persistent simulation host stdin closed");
        await Assert.That(starting).IsLessThan(ready);
        await Assert.That(ready).IsLessThan(closed);
        await Assert.That(snapshot.StandardError[starting]).Contains("level=INFO");
        await Assert.That(snapshot.StandardError[ready]).Contains("level=WARN");
        await Assert.That(snapshot.StandardError[closed]).Contains("level=INFO");

        Console.WriteLine(
            "persistent-host-diagnostics: " + snapshot.StandardError[starting] +
            " | " + snapshot.StandardError[ready] +
            " | " + snapshot.StandardError[closed]);
    }

    /// <summary>
    /// Starts the host under the port gate and releases the gate at readiness: simulation builds
    /// its network layer eagerly at start, so readiness proves the manifest's backend port is bound.
    /// </summary>
    private static async Task<OpenCodeServer> StartHostAsync(
        TestRunRoot runRoot, OpenCodeServerOutput output, CancellationToken cancellationToken)
    {
        using var gate = await DrivePortGate.AcquireAsync(FileSystem, SimulatedServerLaunch.GateTimeout);
        var launch = SimulatedServerLaunch.Prepare(FileSystem, runRoot);
        return await OpenCodeServer.StartAsync(launch.Options(output), cancellationToken);
    }

    /// <summary>
    /// The milestone's index in the final stderr snapshot. A missing milestone fails naming
    /// whether the snapshot lost content: truncation that eats promised evidence is a failure
    /// of this proof, never an excuse for it.
    /// </summary>
    private static int RequireIndex(OpenCodeServerOutputSnapshot snapshot, string milestone)
    {
        for (var index = 0; index < snapshot.StandardError.Count; index++)
        {
            if (snapshot.StandardError[index].Contains(milestone, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException(
            $"The persistent simulation host did not emit '{milestone}' on stderr " +
            $"(stderr truncated: {snapshot.StandardErrorTruncated}).");
    }
}
