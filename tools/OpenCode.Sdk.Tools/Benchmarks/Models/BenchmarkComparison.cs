namespace OpenCode.Sdk.Tools.Benchmarks.Models;

/// <summary>Joined result of two benchmark runs, with one-sided cases reported rather than dropped.</summary>
internal sealed record BenchmarkComparison
{
    public required IReadOnlyList<BenchmarkComparisonRow> Rows { get; init; }

    public required IReadOnlyList<BenchmarkRunCase> BeforeOnly { get; init; }

    public required IReadOnlyList<BenchmarkRunCase> AfterOnly { get; init; }
}
