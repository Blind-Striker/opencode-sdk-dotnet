using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class ModelEmitterTests
{
    [Test]
    public async Task Emit_Should_Produce_Immutable_Annotated_Models()
    {
        var sources = ModelEmitter.Emit(EmitterPlanFixture.CreateModelSnapshot());

        await Verify(EmitterSnapshot.Create(sources));
    }
}
