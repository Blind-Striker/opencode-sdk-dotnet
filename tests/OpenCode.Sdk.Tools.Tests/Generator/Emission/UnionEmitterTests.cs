using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class UnionEmitterTests
{
    [Test]
    public async Task Emit_Should_Produce_Tolerant_Marked_Union_Converters()
    {
        var sources = UnionEmitter.Emit(EmitterPlanFixture.CreateUnionSnapshot());

        await Verify(EmitterSnapshot.Create(sources));
    }

    [Test]
    public async Task Emit_Should_Declare_A_Union_As_An_Interface()
    {
        var source = EmitterSnapshot.Create(UnionEmitter.Emit(EmitterPlanFixture.CreateUnionSnapshot()));

        await Assert.That(source).Contains("public interface IExamplePhase : IExampleEvent");
        await Assert.That(source).DoesNotContain("abstract record");
        await Assert.That(source).Contains("public sealed record UnknownExamplePhase : IExamplePhase");
    }

    [Test]
    public async Task Emit_Should_Leave_Null_To_Outer_Metadata_And_Guard_Foreign_Fixed_Markers()
    {
        var source = EmitterSnapshot.Create(UnionEmitter.Emit(EmitterPlanFixture.CreateUnionSnapshot()));

        var carrier = source[source.IndexOf("class UnknownExamplePhaseJsonConverter", StringComparison.Ordinal)..];
        await Assert.That(carrier).DoesNotContain("HandleNull");
        await Assert.That(carrier).DoesNotContain("payload cannot be null");
        await Assert.That(carrier).Contains("must be 'phase'");
    }
}
