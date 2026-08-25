using System.Globalization;
using System.Text;
using OpenCode.Sdk.Tools.Benchmarks.Models;

namespace OpenCode.Sdk.Tools.Benchmarks;

/// <summary>Renders a benchmark comparison as the repository's CSV extract shape.</summary>
internal static class BenchmarkComparisonCsvComposer
{
    public static string Compose(BenchmarkComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var builder = new StringBuilder();
        builder.Append("\"Case\",\"Runtime\",\"AllocBefore\",\"AllocAfter\",\"AllocDelta\",\"TimeRatio\"\n");
        foreach (var row in comparison.Rows)
        {
            builder
                .Append(Quote(row.CaseLabel)).Append(',')
                .Append(Quote(row.Runtime)).Append(',')
                .Append(Quote(row.AllocatedBefore.ToString(CultureInfo.InvariantCulture))).Append(',')
                .Append(Quote(row.AllocatedAfter.ToString(CultureInfo.InvariantCulture))).Append(',')
                .Append(Quote(row.AllocatedDelta.ToString(CultureInfo.InvariantCulture))).Append(',')
                .Append(Quote(row.TimeRatio.ToString("0.00", CultureInfo.InvariantCulture))).Append('\n');
        }

        return builder.ToString();
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
