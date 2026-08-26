using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion;

/// <summary>
/// The curation-gated identity admit (ADR-0013): a reason-bearing row maps an upstream
/// identity defect onto its intended identity at ingestion; everything else stays refused.
/// </summary>
public sealed class OperationIdentityMappingTests
{
    [Test]
    public async Task Project_Should_Admit_A_Mapped_Identity_As_The_Intended_Identity()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec
            .WithOperation("server.experimental.persistentPty.list", path: "/api/experimental/persistent-pty"));

        var result = await host.ProjectAsync(scenario, Identities(("server.experimental.persistentPty.list", "v2.persistentPty.list")));

        await Assert.That(result.Operations[0].OperationId).IsEqualTo("v2.persistentPty.list");
        await Assert.That(result.Operations[0].Segments[0]).IsEqualTo("persistentPty");
        await Assert.That(result.Operations[0].Segments[1]).IsEqualTo("list");
    }

    [Test]
    public async Task Project_Should_Refuse_An_Off_Convention_Id_The_Map_Does_Not_Name()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec
            .WithOperation("server.experimental.persistentPty.list", path: "/api/experimental/persistent-pty")
            .WithOperation("server.experimental.persistentPty.create", path: "/api/experimental/persistent-pty/create", method: "post"));

        var ex = await host.ProjectExpectingRefusalAsync(
            scenario,
            Identities(("server.experimental.persistentPty.list", "v2.persistentPty.list")));

        await Assert.That(ex.Message).Contains("server.experimental.persistentPty.create");
        await Assert.That(ex.Message).Contains("protocol prefix");
    }

    [Test]
    public async Task Project_Should_Refuse_A_Mapped_Identity_Colliding_With_An_Existing_Operation()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec
            .WithOperation("v2.pty.list", path: "/api/pty")
            .WithOperation("server.experimental.pty.list", path: "/api/experimental/pty"));

        var ex = await host.ProjectExpectingRefusalAsync(scenario, Identities(("server.experimental.pty.list", "v2.pty.list")));

        await Assert.That(ex.Message).Contains("v2.pty.list");
        await Assert.That(ex.Message).Contains("collides");
    }

    [Test]
    public async Task Project_Should_Refuse_An_Identity_Row_Whose_Subject_Is_Absent()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithOperation("v2.health.get"));

        var ex = await host.ProjectExpectingRefusalAsync(scenario, Identities(("server.experimental.gone.list", "v2.gone.list")));

        await Assert.That(ex.Message).Contains("server.experimental.gone.list");
        await Assert.That(ex.Message).Contains("retire the row");
    }

    private static Dictionary<string, string> Identities(params (string Subject, string Identity)[] rows)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (subject, identity) in rows)
        {
            map[subject] = identity;
        }

        return map;
    }
}
