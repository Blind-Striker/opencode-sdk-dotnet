using System.Globalization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace OpenCode.Sdk.Performance.Tests.Support.Columns;

/// <summary>
/// A summary column derived from the case's <see cref="WireFixture"/> and its exact measured
/// allocation, so wire bytes, item counts, and allocation amplification appear in every
/// ordinary BenchmarkDotNet report beside the allocated column.
/// </summary>
internal abstract class WireFixtureColumn : IColumn
{
    private const string Unavailable = "-";

    public string Id => GetType().Name;

    public abstract string ColumnName { get; }

    public bool AlwaysShow => false;

    public ColumnCategory Category => ColumnCategory.Custom;

    public abstract int PriorityInCategory { get; }

    public bool IsNumeric => true;

    public abstract UnitType UnitType { get; }

    public abstract string Legend { get; }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => GetValue(summary, benchmarkCase, SummaryStyle.Default);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(benchmarkCase);
        return FindFixture(benchmarkCase) is not { } fixture
            ? Unavailable
            : Format(fixture, AllocatedBytes(summary, benchmarkCase)) ?? Unavailable;
    }

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public bool IsAvailable(Summary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return summary.BenchmarksCases.Any(static benchmarkCase => FindFixture(benchmarkCase) is not null);
    }

    /// <summary>Formats the column value, or <see langword="null"/> when it cannot be computed for this case.</summary>
    protected abstract string? Format(WireFixture fixture, long? allocatedBytes);

    protected static string Integer(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    protected static string Ratio(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static WireFixture? FindFixture(BenchmarkCase benchmarkCase) =>
        benchmarkCase.Parameters.Items.Select(static parameter => parameter.Value).OfType<WireFixture>().FirstOrDefault();

    private static long? AllocatedBytes(Summary summary, BenchmarkCase benchmarkCase) =>
        summary.HasReport(benchmarkCase) ? summary[benchmarkCase]?.GcStats.GetBytesAllocatedPerOperation(benchmarkCase) : null;
}
