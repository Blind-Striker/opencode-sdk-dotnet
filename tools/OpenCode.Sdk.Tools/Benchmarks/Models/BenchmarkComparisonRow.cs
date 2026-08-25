namespace OpenCode.Sdk.Tools.Benchmarks.Models;

/// <summary>Before/after evidence for one benchmark case present in both runs.</summary>
internal sealed record BenchmarkComparisonRow
{
    public required string CaseLabel { get; init; }

    public required string Runtime { get; init; }

    public required long AllocatedBefore { get; init; }

    public required long AllocatedAfter { get; init; }

    public required long AllocatedDelta { get; init; }

    public required double TimeRatio { get; init; }
}
