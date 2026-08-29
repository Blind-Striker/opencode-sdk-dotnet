namespace OpenCode.Sdk.Tools.Benchmarks.Models;

/// <summary>Before/after evidence for one benchmark case present in both runs.</summary>
internal sealed record BenchmarkComparisonRow
{
    public required string CaseLabel { get; init; }

    public required string Runtime { get; init; }

    public required long AllocatedBefore { get; init; }

    public required long AllocatedAfter { get; init; }

    public required long AllocatedDelta { get; init; }

    /// <summary>After median over before median, or <see langword="null"/> when either leg carries
    /// no positive median timing — allocation still compares, only the ratio is unavailable.</summary>
    public required double? TimeRatio { get; init; }
}
