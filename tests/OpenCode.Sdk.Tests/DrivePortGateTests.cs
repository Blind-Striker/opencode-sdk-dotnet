using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The gate is what keeps the day-one blocking simulated-session suite from racing itself across
/// target-framework legs, so its two load-bearing properties - mutual exclusion, and a bounded
/// loud failure rather than an unbounded wait - are pinned here. A real file system with a
/// per-test path is deliberate: the contract is an operating-system file lock, and every
/// assertion below stays hermetic because no test touches the shared production gate path.
/// </summary>
public sealed class DrivePortGateTests
{
    private static readonly RealFileSystem FileSystem = new();

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(1);

    private static string GatePath() =>
        FileSystem.Path.Combine(FileSystem.Path.GetTempPath(), "opencode-sdk-tests-gate-" + Guid.NewGuid().ToString("N"));

    [Test]
    public async Task AcquireAsync_Should_Refuse_A_Second_Holder_Within_Its_Bound()
    {
        var path = GatePath();
        using var first = await DrivePortGate.AcquireAsync(FileSystem, path, ShortTimeout);

        _ = await Assert.That(async () => await DrivePortGate.AcquireAsync(FileSystem, path, ShortTimeout))
            .Throws<TimeoutException>();
    }

    [Test]
    public async Task AcquireAsync_Should_Hand_The_Gate_On_After_The_Holder_Releases()
    {
        var path = GatePath();
        var first = await DrivePortGate.AcquireAsync(FileSystem, path, ShortTimeout);
        first.Dispose();

        using var second = await DrivePortGate.AcquireAsync(FileSystem, path, ShortTimeout);

        await Assert.That(FileSystem.File.Exists(path)).IsTrue();
    }
}
