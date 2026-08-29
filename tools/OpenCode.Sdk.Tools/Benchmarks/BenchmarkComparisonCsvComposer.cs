using System.Globalization;
using System.Text;
using OpenCode.Sdk.Tools.Benchmarks.Models;

namespace OpenCode.Sdk.Tools.Benchmarks;

/// <summary>
/// Renders a benchmark comparison as the repository's CSV extract shape. Every row carries a
/// <c>Status</c> of <c>Matched</c>, <c>BeforeOnly</c>, or <c>AfterOnly</c> — named after
/// <see cref="BenchmarkComparison"/>'s own <c>Rows</c>/<c>BeforeOnly</c>/<c>AfterOnly</c> shape — so a
/// reader can tell which columns apply without guessing from blank cells. Matched rows populate
/// <c>AllocBefore</c>, <c>AllocAfter</c>, <c>AllocDelta</c>, and <c>TimeRatio</c>, leaving
/// <c>MedianNanoseconds</c> blank (a ratio needs two sides, so a one-sided case cannot have one).
/// One-sided rows populate only the allocation side they actually have plus their own exact
/// <c>MedianNanoseconds</c> (the honest one-sided timing figure), leaving the other allocation
/// column, <c>AllocDelta</c>, and <c>TimeRatio</c> blank — there is no counterpart to diff or ratio
/// against. Rows appear in three sections, each already sorted by the composer: matched, then
/// before-only, then after-only.
/// </summary>
internal static class BenchmarkComparisonCsvComposer
{
    private const string Header =
        "\"Case\",\"Runtime\",\"Status\",\"AllocBefore\",\"AllocAfter\",\"AllocDelta\",\"TimeRatio\",\"MedianNanoseconds\"\n";

    public static string Compose(BenchmarkComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var builder = new StringBuilder();
        builder.Append(Header);
        foreach (var row in comparison.Rows)
        {
            AppendRow(builder, MatchedRow(row));
        }

        foreach (var runCase in comparison.BeforeOnly)
        {
            AppendRow(builder, OneSidedRow(runCase, "BeforeOnly", allocatesBefore: true));
        }

        foreach (var runCase in comparison.AfterOnly)
        {
            AppendRow(builder, OneSidedRow(runCase, "AfterOnly", allocatesBefore: false));
        }

        return builder.ToString();
    }

    private static CsvRow MatchedRow(BenchmarkComparisonRow row) => new()
    {
        CaseLabel = row.CaseLabel,
        Runtime = row.Runtime,
        Status = "Matched",
        AllocBefore = row.AllocatedBefore.ToString(CultureInfo.InvariantCulture),
        AllocAfter = row.AllocatedAfter.ToString(CultureInfo.InvariantCulture),
        AllocDelta = row.AllocatedDelta.ToString(CultureInfo.InvariantCulture),
        TimeRatio = row.TimeRatio.ToString("0.00", CultureInfo.InvariantCulture),
        MedianNanoseconds = string.Empty,
    };

    private static CsvRow OneSidedRow(BenchmarkRunCase runCase, string status, bool allocatesBefore)
    {
        var allocatedBytes = runCase.AllocatedBytes.ToString(CultureInfo.InvariantCulture);
        return new CsvRow
        {
            CaseLabel = runCase.CaseLabel,
            Runtime = runCase.Runtime,
            Status = status,
            AllocBefore = allocatesBefore ? allocatedBytes : string.Empty,
            AllocAfter = allocatesBefore ? string.Empty : allocatedBytes,
            AllocDelta = string.Empty,
            TimeRatio = string.Empty,
            MedianNanoseconds = runCase.MedianNanoseconds.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static void AppendRow(StringBuilder builder, CsvRow row)
    {
        builder
            .Append(Quote(row.CaseLabel)).Append(',')
            .Append(Quote(row.Runtime)).Append(',')
            .Append(Quote(row.Status)).Append(',')
            .Append(Quote(row.AllocBefore)).Append(',')
            .Append(Quote(row.AllocAfter)).Append(',')
            .Append(Quote(row.AllocDelta)).Append(',')
            .Append(Quote(row.TimeRatio)).Append(',')
            .Append(Quote(row.MedianNanoseconds)).Append('\n');
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>One already-formatted, invariant-culture CSV row awaiting quoting.</summary>
    private sealed record CsvRow
    {
        public required string CaseLabel { get; init; }

        public required string Runtime { get; init; }

        public required string Status { get; init; }

        public required string AllocBefore { get; init; }

        public required string AllocAfter { get; init; }

        public required string AllocDelta { get; init; }

        public required string TimeRatio { get; init; }

        public required string MedianNanoseconds { get; init; }
    }
}
