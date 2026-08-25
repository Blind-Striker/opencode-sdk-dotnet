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
            "\"Case\",\"Runtime\",\"AllocBefore\",\"AllocAfter\",\"AllocDelta\",\"TimeRatio\"\n"
            + "\"Health/GetHealthAsync [Fixture=health]\",\".NET 10.0\",\"2104\",\"2376\",\"272\",\"1.08\"\n"
            + "\"Health/GetHealthAsync [Fixture=health]\",\".NET Framework 4.7.2\",\"6432\",\"4686\",\"-1746\",\"0.40\"\n");
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
}
