using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Reports;
using OpenCode.Sdk.Performance.Tests.Support.Columns;
using Perfolizer.Metrology;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// The suite-wide configuration: exact bytes instead of rounded KB, the wire-fixture figures as
/// metrics (rendered beside the allocation column and exported into the full JSON), the derived
/// allocation-per-item and amplification columns, and the full JSON export so raw per-operation
/// bytes and measurements survive every run without re-deriving them from prose.
/// </summary>
internal static class BenchmarkConfig
{
    /// <summary>Wide enough that fixture names print whole instead of being elided in the middle.</summary>
    private const int MaxParameterColumnWidth = 40;

    public static IConfig Create() =>
        ManualConfig.Create(DefaultConfig.Instance)
            .WithSummaryStyle(SummaryStyle.Default.WithSizeUnit(SizeUnit.B).WithMaxParameterColumnWidth(MaxParameterColumnWidth))
            .AddExporter(JsonExporter.Full)
            .AddDiagnoser(new WireFixtureDiagnoser())
            .AddColumn(
                new AllocatedPerItemColumn(),
                new AllocationAmplificationColumn());
}
