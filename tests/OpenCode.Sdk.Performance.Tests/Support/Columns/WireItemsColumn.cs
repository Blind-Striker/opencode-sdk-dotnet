using BenchmarkDotNet.Columns;

namespace OpenCode.Sdk.Performance.Tests.Support.Columns;

/// <summary>How many logical items (payloads or frames) one operation consumes.</summary>
internal sealed class WireItemsColumn : WireFixtureColumn
{
    public override string ColumnName => "Items";

    public override int PriorityInCategory => 1;

    public override UnitType UnitType => UnitType.Dimensionless;

    public override string Legend => "Payloads or frames consumed per operation";

    protected override string Format(WireFixture fixture, long? allocatedBytes) => Integer(fixture.Items);
}
