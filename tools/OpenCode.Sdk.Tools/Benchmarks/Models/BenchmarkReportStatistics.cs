namespace OpenCode.Sdk.Tools.Benchmarks.Models;

/// <summary>Timing statistics of an exported benchmark case, in nanoseconds.</summary>
internal sealed record BenchmarkReportStatistics
{
    public required double Median { get; init; }
}
