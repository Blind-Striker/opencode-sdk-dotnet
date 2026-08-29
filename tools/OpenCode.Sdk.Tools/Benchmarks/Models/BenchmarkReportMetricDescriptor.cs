namespace OpenCode.Sdk.Tools.Benchmarks.Models;

/// <summary>Consumed slice of an exported metric's descriptor; the id alone keys the metric.</summary>
internal sealed record BenchmarkReportMetricDescriptor
{
    public required string Id { get; init; }
}
