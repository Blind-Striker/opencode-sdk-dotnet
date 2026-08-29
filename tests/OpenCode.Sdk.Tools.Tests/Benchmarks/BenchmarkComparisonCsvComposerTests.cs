using OpenCode.Sdk.Tools.Benchmarks;
using OpenCode.Sdk.Tools.Benchmarks.Models;

namespace OpenCode.Sdk.Tools.Tests.Benchmarks;

public sealed class BenchmarkComparisonCsvComposerTests
{
    [Test]
    public async Task Compose_Should_Render_The_Extract_Shape_With_Invariant_Formatting()
    {
        var comparison = new BenchmarkComparison
        {
            Rows =
            [
                Row("Health/GetHealthAsync [Fixture=health]", ".NET 10.0", 2104, 2376, timeRatio: 1.0764),
                Row("Health/GetHealthAsync [Fixture=health]", ".NET Framework 4.7.2", 6432, 4686, timeRatio: 0.399),
            ],
            BeforeOnly = [],
            AfterOnly = [],
        };

        var csv = BenchmarkComparisonCsvComposer.Compose(comparison);

        await Assert.That(csv).IsEqualTo(
            "\"Case\",\"Runtime\",\"Status\",\"AllocBefore\",\"AllocAfter\",\"AllocDelta\",\"TimeRatio\",\"MedianNanoseconds\"\n"
            + "\"Health/GetHealthAsync [Fixture=health]\",\".NET 10.0\",\"Matched\",\"2104\",\"2376\",\"272\",\"1.08\",\"\"\n"
            + "\"Health/GetHealthAsync [Fixture=health]\",\".NET Framework 4.7.2\",\"Matched\",\"6432\",\"4686\",\"-1746\",\"0.40\",\"\"\n");
    }

    [Test]
    public async Task Compose_Should_Double_Embedded_Quotes()
    {
        var comparison = new BenchmarkComparison
        {
            Rows = [Row("Health/Get\"Health\"Async", ".NET 10.0", 100, 100, timeRatio: 1.0)],
            BeforeOnly = [],
            AfterOnly = [],
        };

        var csv = BenchmarkComparisonCsvComposer.Compose(comparison);

        await Assert.That(csv).Contains("\"Health/Get\"\"Health\"\"Async\"");
    }

    [Test]
    public async Task Compose_Should_Render_One_Sided_Cases_With_Status_And_Exact_Median()
    {
        var comparison = new BenchmarkComparison
        {
            Rows = [Row("Health/GetHealthAsync [Fixture=health]", ".NET 10.0", 2104, 2376, timeRatio: 1.0764)],
            BeforeOnly = [RunCase("Health", "Deserialize", "Fixture=health", ".NET 10.0", allocatedBytes: 256, medianNanoseconds: 401.5)],
            AfterOnly = [RunCase("Health", "AdaptSuccess", "Fixture=health", ".NET 10.0", allocatedBytes: 304, medianNanoseconds: 420.25)],
        };

        var csv = BenchmarkComparisonCsvComposer.Compose(comparison);

        await Assert.That(csv).IsEqualTo(
            "\"Case\",\"Runtime\",\"Status\",\"AllocBefore\",\"AllocAfter\",\"AllocDelta\",\"TimeRatio\",\"MedianNanoseconds\"\n"
            + "\"Health/GetHealthAsync [Fixture=health]\",\".NET 10.0\",\"Matched\",\"2104\",\"2376\",\"272\",\"1.08\",\"\"\n"
            + "\"Health/Deserialize [Fixture=health]\",\".NET 10.0\",\"BeforeOnly\",\"256\",\"\",\"\",\"\",\"401.5\"\n"
            + "\"Health/AdaptSuccess [Fixture=health]\",\".NET 10.0\",\"AfterOnly\",\"\",\"304\",\"\",\"\",\"420.25\"\n");
    }

    [Test]
    public async Task Compose_Should_Order_Sections_As_Matched_Then_BeforeOnly_Then_AfterOnly()
    {
        var comparison = new BenchmarkComparison
        {
            Rows =
            [
                Row("Health/AdaptSuccess [Fixture=health]", ".NET 10.0", 300, 304, timeRatio: 0.98),
                Row("Health/GetHealthAsync [Fixture=health]", ".NET 10.0", 2104, 2376, timeRatio: 1.08),
            ],
            BeforeOnly =
            [
                RunCase("Health", "Deserialize", "Fixture=health", ".NET 10.0", allocatedBytes: 256, medianNanoseconds: 401.5),
                RunCase("Health", "Deserialize", "Fixture=health", ".NET Framework 4.7.2", allocatedBytes: 512, medianNanoseconds: 4200.0),
            ],
            AfterOnly =
            [
                RunCase("Health", "NewRung", "Fixture=health", ".NET 10.0", allocatedBytes: 128, medianNanoseconds: 210.0),
                RunCase("Health", "NewRung", "Fixture=health", ".NET Framework 4.7.2", allocatedBytes: 256, medianNanoseconds: 2100.0),
            ],
        };

        var csv = BenchmarkComparisonCsvComposer.Compose(comparison);

        var caseColumn = csv
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Split(',')[0])
            .ToArray();
        await Assert.That(caseColumn.SequenceEqual(
            [
                "\"Health/AdaptSuccess [Fixture=health]\"",
                "\"Health/GetHealthAsync [Fixture=health]\"",
                "\"Health/Deserialize [Fixture=health]\"",
                "\"Health/Deserialize [Fixture=health]\"",
                "\"Health/NewRung [Fixture=health]\"",
                "\"Health/NewRung [Fixture=health]\"",
            ],
            StringComparer.Ordinal)).IsTrue();
    }

    private static BenchmarkComparisonRow Row(string caseLabel, string runtime, long allocatedBefore, long allocatedAfter, double timeRatio) =>
        new()
        {
            CaseLabel = caseLabel,
            Runtime = runtime,
            AllocatedBefore = allocatedBefore,
            AllocatedAfter = allocatedAfter,
            AllocatedDelta = allocatedAfter - allocatedBefore,
            TimeRatio = timeRatio,
        };

    private static BenchmarkRunCase RunCase(string family, string method, string parameters, string runtime, long allocatedBytes, double medianNanoseconds) =>
        new()
        {
            FullName = $"OpenCode.Sdk.Performance.Tests.Benchmarks.{family}Benchmarks.{method}({parameters})",
            Family = family,
            Method = method,
            Parameters = parameters,
            Runtime = runtime,
            AllocatedBytes = allocatedBytes,
            MedianNanoseconds = medianNanoseconds,
        };
}
