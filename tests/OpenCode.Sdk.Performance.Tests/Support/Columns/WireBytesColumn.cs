using BenchmarkDotNet.Columns;

namespace OpenCode.Sdk.Performance.Tests.Support.Columns;

/// <summary>The exact body bytes one operation consumes from the wire.</summary>
internal sealed class WireBytesColumn : WireFixtureColumn
{
    public override string ColumnName => "Wire B";

    public override int PriorityInCategory => 0;

    public override UnitType UnitType => UnitType.Size;

    public override string Legend => "Exact wire body bytes one operation consumes (envelope/framing included)";

    protected override string Format(WireFixture fixture, long? allocatedBytes) => Integer(fixture.WireBytes);
}
