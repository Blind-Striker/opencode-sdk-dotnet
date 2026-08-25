using OpenCode.Sdk.Tools.Benchmarks;
using OpenCode.Sdk.Tools.Benchmarks.Models;

namespace OpenCode.Sdk.Tools.Tests.Benchmarks;

public sealed class BenchmarkComparisonComposerTests
{
    [Test]
    public async Task Compose_Should_Pair_Cases_On_Full_Name_And_Runtime()
    {
        var before = new[]
        {
            Case("GetHealthAsync", ".NET 10.0", allocatedBytes: 2104, medianNanoseconds: 1000.0),
            Case("GetHealthAsync", ".NET Framework 4.7.2", allocatedBytes: 6432, medianNanoseconds: 16000.0),
        };
        var after = new[]
        {
            Case("GetHealthAsync", ".NET 10.0", allocatedBytes: 2376, medianNanoseconds: 250.0),
        };

        var comparison = BenchmarkComparisonComposer.Compose(before, after);

        var row = comparison.Rows.Single();
        await Assert.That(row.CaseLabel).IsEqualTo("Health/GetHealthAsync [Fixture=health]");
        await Assert.That(row.Runtime).IsEqualTo(".NET 10.0");
        await Assert.That(row.AllocatedBefore).IsEqualTo(2104L);
        await Assert.That(row.AllocatedAfter).IsEqualTo(2376L);
        await Assert.That(row.AllocatedDelta).IsEqualTo(272L);
        await Assert.That(row.TimeRatio).IsEqualTo(0.25);
    }

    [Test]
    public async Task Compose_Should_Report_Cases_Missing_From_Either_Side()
    {
        var before = new[]
        {
            Case("GetHealthAsync", ".NET 10.0", allocatedBytes: 2104, medianNanoseconds: 1000.0),
            Case("Deserialize", ".NET 10.0", allocatedBytes: 256, medianNanoseconds: 400.0),
        };
        var after = new[]
        {
            Case("GetHealthAsync", ".NET 10.0", allocatedBytes: 2376, medianNanoseconds: 980.0),
            Case("AdaptSuccess", ".NET 10.0", allocatedBytes: 304, medianNanoseconds: 420.0),
        };

        var comparison = BenchmarkComparisonComposer.Compose(before, after);

        await Assert.That(comparison.Rows.Count).IsEqualTo(1);
        await Assert.That(comparison.BeforeOnly.Single().Method).IsEqualTo("Deserialize");
        await Assert.That(comparison.AfterOnly.Single().Method).IsEqualTo("AdaptSuccess");
    }

    [Test]
    public async Task Compose_Should_Order_Rows_By_Case_Label_Then_Runtime()
    {
        var before = new[]
        {
            Case("GetHealthAsync", ".NET Framework 4.7.2", allocatedBytes: 6432, medianNanoseconds: 16000.0),
            Case("GetHealthAsync", ".NET 10.0", allocatedBytes: 2104, medianNanoseconds: 1000.0),
            Case("AdaptSuccess", ".NET 10.0", allocatedBytes: 304, medianNanoseconds: 420.0),
        };
        var after = new[]
        {
            Case("GetHealthAsync", ".NET 10.0", allocatedBytes: 2376, medianNanoseconds: 980.0),
            Case("AdaptSuccess", ".NET 10.0", allocatedBytes: 304, medianNanoseconds: 410.0),
            Case("GetHealthAsync", ".NET Framework 4.7.2", allocatedBytes: 4686, medianNanoseconds: 6000.0),
        };

        var comparison = BenchmarkComparisonComposer.Compose(before, after);

        var order = comparison.Rows.Select(row => $"{row.CaseLabel}|{row.Runtime}").ToArray();
        await Assert.That(order.SequenceEqual(
            [
                "Health/AdaptSuccess [Fixture=health]|.NET 10.0",
                "Health/GetHealthAsync [Fixture=health]|.NET 10.0",
                "Health/GetHealthAsync [Fixture=health]|.NET Framework 4.7.2",
            ],
            StringComparer.Ordinal)).IsTrue();
    }

    private static BenchmarkRunCase Case(string method, string runtime, long allocatedBytes, double medianNanoseconds) =>
        new()
        {
            FullName = $"OpenCode.Sdk.Performance.Tests.Benchmarks.HealthBenchmarks.{method}(Fixture: health)",
            Family = "Health",
            Method = method,
            Parameters = "Fixture=health",
            Runtime = runtime,
            AllocatedBytes = allocatedBytes,
            MedianNanoseconds = medianNanoseconds,
        };
}
