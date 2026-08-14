using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class OptionsEmitterTests
{
    [Test]
    public async Task Emit_Should_Render_The_Options_Records()
    {
        var sources = OptionsEmitter.Emit(EmitterPlanFixture.CreateClientPlans());

        await Verify(EmitterSnapshot.Create(sources));
    }
}
