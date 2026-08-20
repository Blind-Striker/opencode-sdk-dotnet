using BenchmarkDotNet.Columns;

namespace OpenCode.Sdk.Performance.Tests.Support.Columns;

/// <summary>Exact allocated bytes divided by wire bytes: how many bytes the SDK allocates per byte it receives.</summary>
internal sealed class AllocationAmplificationColumn : WireFixtureColumn
{
    public override string ColumnName => "Alloc/Wire";

    public override int PriorityInCategory => 4;

    public override UnitType UnitType => UnitType.Dimensionless;

    public override string Legend => "Allocated bytes per operation divided by wire bytes consumed (allocation amplification)";

    protected override string? Format(WireFixture fixture, long? allocatedBytes) =>
        allocatedBytes is { } allocated && fixture.WireBytes > 0 ? Ratio(allocated / (double)fixture.WireBytes) + "x" : null;
}
