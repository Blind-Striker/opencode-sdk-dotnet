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
}
