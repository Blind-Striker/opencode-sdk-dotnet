namespace OpenCode.Sdk.Tools.Benchmarks.Models;

/// <summary>One exported metric of a benchmark case: the exact value, keyed by its descriptor.</summary>
internal sealed record BenchmarkReportMetric
{
    public required BenchmarkReportMetricDescriptor Descriptor { get; init; }

    public required double Value { get; init; }
}
