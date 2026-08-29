namespace OpenCode.Sdk.Tools.Benchmarks.Models;

/// <summary>One benchmark case as BenchmarkDotNet exports it; unmapped members are skipped.</summary>
internal sealed record BenchmarkReportCase
{
    public required string DisplayInfo { get; init; }

    public required string Type { get; init; }

    public required string Method { get; init; }

    public string Parameters { get; init; } = string.Empty;

    public required string FullName { get; init; }

    public BenchmarkReportStatistics? Statistics { get; init; }

    public BenchmarkReportMemory? Memory { get; init; }

    /// <summary>The exported metrics. The exporter omits the member for a case with none, and the
    /// wire ids are optional within it, so the reader treats the whole member as optional.</summary>
    public IReadOnlyList<BenchmarkReportMetric>? Metrics { get; init; }
}
