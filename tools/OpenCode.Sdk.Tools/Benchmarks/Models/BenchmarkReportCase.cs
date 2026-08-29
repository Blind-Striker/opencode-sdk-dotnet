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

    /// <summary>The exported metrics; absent in exports predating BenchmarkDotNet's metric output.</summary>
    public IReadOnlyList<BenchmarkReportMetric>? Metrics { get; init; }
}
