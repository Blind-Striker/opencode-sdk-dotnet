using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class CallableSurfaceCompilationTests
{
    [Test]
    public async Task Emit_Should_Produce_A_Compilable_Callable_Surface()
    {
        var plan = EmitterPlanFixture.Create();
        var sources = new List<GeneratedSource>();
        sources.AddRange(SourceEmitter.Emit(plan));
        sources.Add(RoutesEmitter.Emit(plan.Clients));
        sources.AddRange(EnvelopeEmitter.Emit(plan.Clients));
        sources.AddRange(ResponseAdapterEmitter.Emit(plan.Clients));
        sources.AddRange(ClientEmitter.Emit(plan.Clients));

        var diagnostics = await GeneratedSourceCompiler.CompileWithSdkCoreAsync(sources);

        await Assert.That(diagnostics).IsEmpty();
    }
}
