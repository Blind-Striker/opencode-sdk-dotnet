namespace OpenCode.Sdk.Tools.Benchmarks.Models;

/// <summary>Consumed slice of a BenchmarkDotNet <c>*-report-full.json</c> export.</summary>
internal sealed record BenchmarkReportDocument
{
    public IReadOnlyList<BenchmarkReportCase>? Benchmarks { get; init; }
}
