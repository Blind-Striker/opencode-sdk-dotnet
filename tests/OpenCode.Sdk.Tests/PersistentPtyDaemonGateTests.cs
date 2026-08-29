using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class PersistentPtyDaemonGateTests
{
    [Test]
    public async Task Resolve_Should_Return_True_When_The_Override_Is_One()
    {
        var result = PersistentPtyDaemonGate.Resolve(static _ => "1");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Resolve_Should_Return_False_When_The_Override_Is_Zero()
    {
        var result = PersistentPtyDaemonGate.Resolve(static _ => "0");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Resolve_Should_Return_The_Platform_Default_When_The_Override_Is_Unset()
    {
        var result = PersistentPtyDaemonGate.Resolve(static _ => null);

        await Assert.That(result).IsEqualTo(!OperatingSystem.IsWindows());
    }
}
