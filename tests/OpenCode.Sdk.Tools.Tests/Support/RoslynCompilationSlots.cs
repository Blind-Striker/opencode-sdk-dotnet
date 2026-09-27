using TUnit.Core.Interfaces;

namespace OpenCode.Sdk.Tools.Tests.Support;

/// <summary>
/// How many whole-SDK compilations (<see cref="GeneratedSourceCompiler"/>) run at once in this test
/// process. Roslyn already spreads one compilation across every core, so running more at once
/// adds no throughput. It only adds memory and garbage-collector pressure, and on a four-vCPU CI
/// runner it starves the test hosts that run beside this one. TUnit starts a test that carries
/// <c>[ParallelLimiter&lt;RoslynCompilationSlots&gt;]</c> only when a slot is free.
/// </summary>
public sealed class RoslynCompilationSlots : IParallelLimit
{
    public int Limit => 2;
}
