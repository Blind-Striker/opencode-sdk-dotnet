using OpenCode.Sdk.Internal.BackgroundService;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// The CLI version-mismatch wrapper: Ignore strips the expected version, Replace keeps it, Error
/// decides from two Discover results without entering the loop on a mismatch.
/// </summary>
public sealed class ServiceVersionPolicyTests
{
    [Test]
    public async Task MatchesVersion_Should_Return_True_When_Nothing_Is_Expected()
    {
        await Assert.That(ServiceVersionPolicy.MatchesVersion("2.0.3", expected: null)).IsTrue();
        await Assert.That(ServiceVersionPolicy.MatchesVersion(reported: null, expected: null)).IsTrue();
    }

    [Test]
    public async Task MatchesVersion_Should_Return_False_When_The_Reported_Version_Is_Missing()
    {
        await Assert.That(ServiceVersionPolicy.MatchesVersion(reported: null, "2.0.3")).IsFalse();
    }

    [Test]
    public async Task MatchesVersion_Should_Return_True_When_The_Reported_Version_Equals_The_Expected()
    {
        await Assert.That(ServiceVersionPolicy.MatchesVersion("2.0.3", "2.0.3")).IsTrue();
    }

    [Test]
    public async Task MatchesVersion_Should_Return_False_When_The_Reported_Version_Differs()
    {
        await Assert.That(ServiceVersionPolicy.MatchesVersion("2.0.3", "2.0.2")).IsFalse();
    }

    [Test]
    public async Task LoopVersion_Should_Be_Null_When_The_Policy_Is_Ignore()
    {
        var policy = new ServiceVersionPolicy(OpenCodeServerVersionPolicy.Ignore, "2.0.3");

        await Assert.That(policy.LoopVersion).IsNull();
        await Assert.That(policy.Matches("2.0.2")).IsTrue();
        await Assert.That(policy.RequiresPreamble).IsFalse();
    }

    [Test]
    public async Task LoopVersion_Should_Keep_The_Expected_Version_When_The_Policy_Is_Replace()
    {
        var policy = new ServiceVersionPolicy(OpenCodeServerVersionPolicy.Replace, "2.0.3");

        await Assert.That(policy.LoopVersion).IsEqualTo("2.0.3");
        await Assert.That(policy.Matches("2.0.3")).IsTrue();
        await Assert.That(policy.Matches("2.0.2")).IsFalse();
        await Assert.That(policy.RequiresPreamble).IsFalse();
    }

    [Test]
    public async Task LoopVersion_Should_Keep_The_Expected_Version_When_The_Policy_Is_Error()
    {
        var policy = new ServiceVersionPolicy(OpenCodeServerVersionPolicy.Error, "2.0.3");

        await Assert.That(policy.LoopVersion).IsEqualTo("2.0.3");
        await Assert.That(policy.RequiresPreamble).IsTrue();
    }

    [Test]
    public async Task DecideErrorPreamble_Should_Return_The_Compatible_Service()
    {
        var matching = Registration("2.0.3");

        var decision = ServiceVersionPolicy.DecideErrorPreamble(matching, existing: matching);

        var returned = await Assert.That(decision).IsTypeOf<ServiceVersionPreamble.ReturnExisting>();
        await Assert.That(returned!.Registration).IsSameReferenceAs(matching);
    }

    [Test]
    public async Task DecideErrorPreamble_Should_Throw_When_A_Service_Is_Running_At_The_Wrong_Version()
    {
        var existing = Registration("2.0.2");

        var decision = ServiceVersionPolicy.DecideErrorPreamble(compatible: null, existing);

        await Assert.That(decision).IsTypeOf<ServiceVersionPreamble.ThrowMismatch>();
    }

    [Test]
    public async Task DecideErrorPreamble_Should_Enter_The_Loop_When_Nothing_Is_Registered()
    {
        var decision = ServiceVersionPolicy.DecideErrorPreamble(compatible: null, existing: null);

        await Assert.That(decision).IsTypeOf<ServiceVersionPreamble.EnterLoop>();
    }

    private static ServiceRegistration Registration(string version) =>
        new("srv_1", version, "http://127.0.0.1:1", new Uri("http://127.0.0.1:1"), 48213, "pw");
}
