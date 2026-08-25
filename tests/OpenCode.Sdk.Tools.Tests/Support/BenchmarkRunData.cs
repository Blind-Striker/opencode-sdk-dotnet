using Testably.Abstractions.Testing;
using Testably.Abstractions.Testing.Initializer;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal static class BenchmarkRunData
{
    public const string BeforeDirectory = ".benchmarks/before";
    public const string AfterDirectory = ".benchmarks/after";
    public const string BeforeReportPath = ".benchmarks/before/results/HealthBenchmarks-report-full.json";
    public const string AfterReportPath = ".benchmarks/after/results/HealthBenchmarks-report-full.json";
    public const string BeforeFixture = "Benchmarks.health-before-report-full.json";
    public const string AfterFixture = "Benchmarks.health-after-report-full.json";

    /// <summary>A store with the fixture before/after runs at their conventional run-folder paths.</summary>
    public static MockFileSystem CreateComparisonStore()
    {
        var loader = new FixtureLoader();
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(
            new FileDescription(BeforeReportPath, loader.Load(BeforeFixture)),
            new FileDescription(AfterReportPath, loader.Load(AfterFixture)));
        return fileSystem;
    }
}
