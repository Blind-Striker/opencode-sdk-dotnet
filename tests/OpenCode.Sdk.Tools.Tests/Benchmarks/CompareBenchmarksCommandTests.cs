using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Sdk.Tools.Tests.Support;
using Spectre.Console;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;
using Testably.Abstractions.Testing;
using Testably.Abstractions.Testing.Initializer;

namespace OpenCode.Sdk.Tools.Tests.Benchmarks;

public sealed class CompareBenchmarksCommandTests
{
    [Test]
    public async Task Execute_Should_Compare_Runs_And_Write_The_Csv_Extract()
    {
        const string outputPath = ".benchmarks/health-comparison.csv";
        var fileSystem = BenchmarkRunData.CreateComparisonStore();
        using var registrar = ToolApp.CreateRegistrar(services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IAnsiConsole, TestConsole>();
        });
        var tester = new CommandAppTester(registrar);
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync(
        [
            "compare-benchmarks",
            BenchmarkRunData.BeforeDirectory,
            BenchmarkRunData.AfterDirectory,
            "--output",
            outputPath,
        ]);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var csv = await fileSystem.File.ReadAllTextAsync(outputPath, CancellationToken.None);
        await Assert.That(csv).StartsWith(
            "\"Case\",\"Runtime\",\"Status\",\"AllocBefore\",\"AllocAfter\",\"AllocDelta\",\"TimeRatio\",\"MedianNanoseconds\"\n");
        await Assert.That(csv).Contains(
            "\"Health/GetHealthAsync [Fixture=health]\",\".NET 10.0\",\"Matched\",\"2104\",\"2376\",\"272\",\"0.98\",\"\"\n");
        await Assert.That(csv).Contains(
            "\"Health/ExecuteWithoutAdapterAsync [Fixture=health]\",\".NET Framework 4.7.2\",\"Matched\",\"6142\",\"4413\",\"-1729\",\"0.37\",\"\"\n");

        // The after run's new rung has no baseline yet — it must still land with exact figures rather
        // than vanish from the durable CSV (ROADMAP.md's compare-benchmarks completeness requirement).
        await Assert.That(csv).Contains(
            "\"Health/GetVersionAsync [Fixture=health]\",\".NET 10.0\",\"AfterOnly\",\"\",\"96\",\"\",\"\",\"645.5\"\n");
    }

    [Test]
    public async Task Execute_Should_Fail_When_The_Runs_Share_No_Cases()
    {
        var fileSystem = new MockFileSystem();
        var loader = new FixtureLoader();
        fileSystem.Initialize().With(
            new FileDescription(BenchmarkRunData.BeforeReportPath, loader.Load(BenchmarkRunData.BeforeFixture)),
            new FileDescription(
                BenchmarkRunData.AfterReportPath,
                loader.Load("Benchmarks.single-job-report-full.json")));
        using var registrar = ToolApp.CreateRegistrar(services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IAnsiConsole, TestConsole>();
        });
        var tester = new CommandAppTester(registrar);
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync(
        [
            "compare-benchmarks",
            BenchmarkRunData.BeforeDirectory,
            BenchmarkRunData.AfterDirectory,
        ]);

        await Assert.That(result.ExitCode).IsEqualTo(1);
    }
}
