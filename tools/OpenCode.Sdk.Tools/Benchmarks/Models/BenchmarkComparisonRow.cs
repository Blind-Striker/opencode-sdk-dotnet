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

    /// <summary>The case's exact wire body bytes. The wire figures describe the fixture rather than
    /// a measurement, so the row carries one triple: the after leg's when that run recorded wire
    /// metrics, otherwise the before leg's (an archived baseline predating them records none), and
    /// <see langword="null"/> when neither run did.</summary>
    public required long? WireBytes { get; init; }

    /// <summary>Payloads or frames consumed per operation; sourced as <see cref="WireBytes"/>.</summary>
    public required long? WireItems { get; init; }

    /// <summary>JSON payload bytes per item; sourced as <see cref="WireBytes"/>.</summary>
    public required long? PayloadBytesPerItem { get; init; }
}
