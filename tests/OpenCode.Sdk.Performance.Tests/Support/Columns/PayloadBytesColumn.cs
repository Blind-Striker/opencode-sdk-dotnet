using BenchmarkDotNet.Columns;

namespace OpenCode.Sdk.Performance.Tests.Support.Columns;

/// <summary>The JSON payload bytes per item, excluding envelope and framing.</summary>
internal sealed class PayloadBytesColumn : WireFixtureColumn
{
    public override string ColumnName => "Payload B/item";

    public override int PriorityInCategory => 2;

    public override UnitType UnitType => UnitType.Size;

    public override string Legend => "JSON payload bytes per item, excluding envelope and SSE framing (average for a mixed body)";

    protected override string? Format(WireFixture fixture, long? allocatedBytes) =>
        fixture.Items is 0 ? null : Integer(fixture.PayloadBytesPerItem);
}
