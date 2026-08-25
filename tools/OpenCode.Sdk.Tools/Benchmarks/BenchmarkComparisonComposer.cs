using OpenCode.Sdk.Tools.Benchmarks.Models;

namespace OpenCode.Sdk.Tools.Benchmarks;

/// <summary>Joins two benchmark runs case-by-case on full name and runtime leg.</summary>
internal static class BenchmarkComparisonComposer
{
    public static BenchmarkComparison Compose(IReadOnlyList<BenchmarkRunCase> before, IReadOnlyList<BenchmarkRunCase> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeByCase = before.ToDictionary(static runCase => (runCase.FullName, runCase.Runtime));
        var rows = new List<BenchmarkComparisonRow>();
        var afterOnly = new List<BenchmarkRunCase>();
        var matchedCases = new HashSet<(string FullName, string Runtime)>();
        foreach (var afterCase in after)
        {
            if (!beforeByCase.TryGetValue((afterCase.FullName, afterCase.Runtime), out var beforeCase))
            {
                afterOnly.Add(afterCase);
                continue;
            }

            matchedCases.Add((afterCase.FullName, afterCase.Runtime));
            rows.Add(new BenchmarkComparisonRow
            {
                CaseLabel = afterCase.CaseLabel,
                Runtime = afterCase.Runtime,
                AllocatedBefore = beforeCase.AllocatedBytes,
                AllocatedAfter = afterCase.AllocatedBytes,
                AllocatedDelta = afterCase.AllocatedBytes - beforeCase.AllocatedBytes,
                TimeRatio = afterCase.MedianNanoseconds / beforeCase.MedianNanoseconds,
            });
        }

        var beforeOnly = before.Where(runCase => !matchedCases.Contains((runCase.FullName, runCase.Runtime)));
        return new BenchmarkComparison
        {
            Rows = [.. rows
                .OrderBy(static row => row.CaseLabel, StringComparer.Ordinal)
                .ThenBy(static row => row.Runtime, StringComparer.Ordinal)],
            BeforeOnly = Sort(beforeOnly),
            AfterOnly = Sort(afterOnly),
        };
    }

    private static IReadOnlyList<BenchmarkRunCase> Sort(IEnumerable<BenchmarkRunCase> cases) =>
    [
        .. cases
            .OrderBy(static runCase => runCase.CaseLabel, StringComparer.Ordinal)
            .ThenBy(static runCase => runCase.Runtime, StringComparer.Ordinal),
    ];
}
