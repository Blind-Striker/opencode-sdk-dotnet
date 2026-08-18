using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class RegistryEmitterTests
{
    [Test]
    public async Task Emit_Should_Produce_The_Source_Generated_Context_Registry()
    {
        var sources = RegistryEmitter.Emit(EmitterPlanFixture.CreateRegistry());
        var snapshot = EmitterSnapshot.Create(sources);

        await Assert.That(snapshot).Contains("UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip");
        await Verify(snapshot);
    }
}
