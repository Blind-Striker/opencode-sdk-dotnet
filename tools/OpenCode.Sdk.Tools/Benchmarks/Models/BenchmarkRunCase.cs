namespace OpenCode.Sdk.Tools.Benchmarks.Models;

/// <summary>One benchmark case of a run, projected to the comparison's axes.</summary>
internal sealed record BenchmarkRunCase
{
    public required string FullName { get; init; }

    public required string Family { get; init; }

    public required string Method { get; init; }

    public required string Parameters { get; init; }

    public required string Runtime { get; init; }

    public required long AllocatedBytes { get; init; }

    /// <summary>The case's median timing, or <see langword="null"/> when the run yielded no positive
    /// median — a constant-folded case measures at the noise floor and carries no usable timing.</summary>
    public required double? MedianNanoseconds { get; init; }

    public string CaseLabel => Parameters.Length == 0
        ? $"{Family}/{Method}"
        : $"{Family}/{Method} [{Parameters}]";
}
