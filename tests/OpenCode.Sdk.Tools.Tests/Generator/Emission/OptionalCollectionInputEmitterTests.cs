using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class OptionalCollectionInputEmitterTests
{
    [Test]
    public async Task Emit_Should_Produce_The_Nullable_Input_Normalizer()
    {
        var source = OptionalCollectionInputEmitter.Emit();

        await Verify(EmitterSnapshot.Create([source]));
    }
}
