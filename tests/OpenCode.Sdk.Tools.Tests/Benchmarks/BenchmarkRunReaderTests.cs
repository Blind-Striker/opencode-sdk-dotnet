using OpenCode.Sdk.Tools.Benchmarks;
using OpenCode.Sdk.Tools.Tests.Support;
using Testably.Abstractions.Testing;
using Testably.Abstractions.Testing.Initializer;

namespace OpenCode.Sdk.Tools.Tests.Benchmarks;

public sealed class BenchmarkRunReaderTests
{
    private readonly FixtureLoader _fixtures = new();

    [Test]
    public async Task ReadAsync_Should_Project_Every_Case_From_The_Results_Folder()
    {
        var reader = new BenchmarkRunReader(BenchmarkRunData.CreateComparisonStore());

        var cases = await reader.ReadAsync(BenchmarkRunData.BeforeDirectory, CancellationToken.None);

        await Assert.That(cases.Count).IsEqualTo(4);
        var healthCase = cases.Single(runCase =>
            runCase.Method == "GetHealthAsync" && runCase.Runtime == ".NET 10.0");
        await Assert.That(healthCase.Family).IsEqualTo("Health");
        await Assert.That(healthCase.Parameters).IsEqualTo("Fixture=health");
        await Assert.That(healthCase.FullName)
            .IsEqualTo("OpenCode.Sdk.Performance.Tests.Benchmarks.HealthBenchmarks.GetHealthAsync(Fixture: health)");
        await Assert.That(healthCase.AllocatedBytes).IsEqualTo(2104L);
        await Assert.That(healthCase.MedianNanoseconds).IsEqualTo(1351.6587257385254);
        await Assert.That(healthCase.CaseLabel).IsEqualTo("Health/GetHealthAsync [Fixture=health]");
    }

    [Test]
    public async Task ReadAsync_Should_Read_Reports_From_The_Directory_Itself_When_No_Results_Folder_Exists()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(
            "runs/flat-report-full.json",
            _fixtures.Load(BenchmarkRunData.BeforeFixture)));
        var reader = new BenchmarkRunReader(fileSystem);

        var cases = await reader.ReadAsync("runs", CancellationToken.None);

        await Assert.That(cases.Count).IsEqualTo(4);
    }

    [Test]
    public async Task ReadAsync_Should_Fall_Back_To_The_Job_Name_When_The_Display_Info_Carries_No_Runtime()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(
            "runs/results/single-report-full.json",
            _fixtures.Load("Benchmarks.single-job-report-full.json")));
        var reader = new BenchmarkRunReader(fileSystem);

        var cases = await reader.ReadAsync("runs", CancellationToken.None);

        await Assert.That(cases.Single().Runtime).IsEqualTo("DefaultJob");
    }

    [Test]
    public async Task ReadAsync_Should_Refuse_A_Missing_Run_Directory()
    {
        var reader = new BenchmarkRunReader(new MockFileSystem());

        var exception = await Assert.That(async () => await reader.ReadAsync("missing", CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("does not exist");
    }

    [Test]
    public async Task ReadAsync_Should_Refuse_A_Run_Directory_Without_Full_Json_Exports()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory("runs");
        var reader = new BenchmarkRunReader(fileSystem);

        var exception = await Assert.That(async () => await reader.ReadAsync("runs", CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("*-report-full.json");
    }

    [Test]
    public async Task ReadAsync_Should_Refuse_A_Case_Without_Memory_Diagnostics()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(
            "runs/results/missing-memory-report-full.json",
            _fixtures.Load("Benchmarks.missing-memory-report-full.json")));
        var reader = new BenchmarkRunReader(fileSystem);

        var exception = await Assert.That(async () => await reader.ReadAsync("runs", CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("memory diagnostics");
    }

    [Test]
    public async Task ReadAsync_Should_Refuse_A_Case_Duplicated_Across_Reports()
    {
        var report = _fixtures.Load(BenchmarkRunData.BeforeFixture);
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(
            new FileDescription("runs/results/first-report-full.json", report),
            new FileDescription("runs/results/second-report-full.json", report));
        var reader = new BenchmarkRunReader(fileSystem);

        var exception = await Assert.That(async () => await reader.ReadAsync("runs", CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("more than once");
    }
}
