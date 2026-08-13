using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class SourceEmitterTests
{
    [Test]
    public async Task Emit_Should_Return_Ordinal_Sorted_Byte_Identical_Source()
    {
        var plan = EmitterPlanFixture.Create();

        var first = SourceEmitter.Emit(plan);
        var second = SourceEmitter.Emit(plan);

        await Assert.That(first.Select(static source => source.RelativePath)
            .SequenceEqual(first.Select(static source => source.RelativePath).Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
        await Assert.That(first.Select(static source => source.Utf8Source.ToArray())
            .Zip(second.Select(static source => source.Utf8Source.ToArray()), static (left, right) => left.SequenceEqual(right))
            .All(static equal => equal)).IsTrue();
    }

    [Test]
    public async Task Emit_Should_Produce_Compilable_Source_For_The_Selected_Pin()
    {
        var plan = await new BindingTestHost().BindPinnedAsync();

        var diagnostics = await GeneratedSourceCompiler.CompileWithSdkCoreAsync(SourceEmitter.Emit(plan));

        await Assert.That(diagnostics).IsEmpty();
    }
}
