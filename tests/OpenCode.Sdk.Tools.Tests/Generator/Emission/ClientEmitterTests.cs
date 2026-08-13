using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class ClientEmitterTests
{
    [Test]
    public async Task Emit_Should_Produce_Virtual_Delegating_Clients()
    {
        var sources = ClientEmitter.Emit(EmitterPlanFixture.CreateClientPlans());

        await Verify(EmitterSnapshot.Create(sources));
    }
}
