using System.IO.Abstractions;
using System.Text.Json;
using OpenCode.Sdk.Tools.Benchmarks.Models;

namespace OpenCode.Sdk.Tools.Benchmarks;

/// <summary>Reads every BenchmarkDotNet full JSON export of one run folder into comparison cases.</summary>
internal sealed class BenchmarkRunReader
{
    private const string ReportPattern = "*-report-full.json";
    private const string BenchmarkTypeSuffix = "Benchmarks";

    private readonly IFileSystem _fileSystem;

    public BenchmarkRunReader(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    public async Task<IReadOnlyList<BenchmarkRunCase>> ReadAsync(string runDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);

        var reportDirectory = ResolveReportDirectory(runDirectory);
        var reportPaths = _fileSystem.Directory
            .GetFiles(reportDirectory, ReportPattern)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (reportPaths.Length == 0)
        {
            throw new InvalidOperationException(
                $"Benchmark run directory '{reportDirectory}' contains no '{ReportPattern}' exports.");
        }

        var cases = new List<BenchmarkRunCase>();
        var seenCases = new HashSet<(string FullName, string Runtime)>();
        foreach (var reportPath in reportPaths)
        {
            foreach (var runCase in await ReadReportAsync(reportPath, cancellationToken).ConfigureAwait(false))
            {
                if (!seenCases.Add((runCase.FullName, runCase.Runtime)))
                {
                    throw new InvalidOperationException(
                        $"Benchmark case '{runCase.FullName}' on '{runCase.Runtime}' appears more than once under '{reportDirectory}'.");
                }

                cases.Add(runCase);
            }
        }

        return cases;
    }

    private string ResolveReportDirectory(string runDirectory)
    {
        var resultsDirectory = _fileSystem.Path.Combine(runDirectory, "results");
        if (_fileSystem.Directory.Exists(resultsDirectory))
        {
            return resultsDirectory;
        }

        if (!_fileSystem.Directory.Exists(runDirectory))
        {
            throw new InvalidOperationException($"Benchmark run directory '{runDirectory}' does not exist.");
        }

        return runDirectory;
    }

    private async Task<IEnumerable<BenchmarkRunCase>> ReadReportAsync(string reportPath, CancellationToken cancellationToken)
    {
        var reportBytes = await _fileSystem.File.ReadAllBytesAsync(reportPath, cancellationToken).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize(reportBytes, BenchmarkReportJsonContext.Default.BenchmarkReportDocument);
        if (document?.Benchmarks is not { Count: > 0 } reportCases)
        {
            throw new InvalidOperationException($"Benchmark report '{reportPath}' declares no benchmark cases.");
        }

        return reportCases.Select(reportCase => ProjectCase(reportPath, reportCase));
    }

    private static BenchmarkRunCase ProjectCase(string reportPath, BenchmarkReportCase reportCase)
    {
        if (reportCase.Memory is not { BytesAllocatedPerOperation: >= 0 } memory)
        {
            throw new InvalidOperationException(
                $"Benchmark case '{reportCase.FullName}' in '{reportPath}' carries no memory diagnostics; run with MemoryDiagnoser enabled.");
        }

        if (reportCase.Statistics is not { Median: > 0 } statistics)
        {
            throw new InvalidOperationException(
                $"Benchmark case '{reportCase.FullName}' in '{reportPath}' carries no positive median timing.");
        }

        return new BenchmarkRunCase
        {
            FullName = reportCase.FullName,
            Family = TrimFamily(reportCase.Type),
            Method = reportCase.Method,
            Parameters = reportCase.Parameters,
            Runtime = ExtractRuntime(reportCase.DisplayInfo),
            AllocatedBytes = memory.BytesAllocatedPerOperation,
            MedianNanoseconds = statistics.Median,
        };
    }

    private static string TrimFamily(string typeName) =>
        typeName.EndsWith(BenchmarkTypeSuffix, StringComparison.Ordinal) && typeName.Length > BenchmarkTypeSuffix.Length
            ? typeName[..^BenchmarkTypeSuffix.Length]
            : typeName;

    private static string ExtractRuntime(string displayInfo)
    {
        const string runtimeMarker = "Runtime=";
        var markerIndex = displayInfo.IndexOf(runtimeMarker, StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            var valueStart = markerIndex + runtimeMarker.Length;
            var valueEnd = displayInfo.IndexOfAny([',', ')'], valueStart);
            return valueEnd < 0 ? displayInfo[valueStart..] : displayInfo[valueStart..valueEnd];
        }

        // "Type.Method: JobDisplay [Parameters]" — without an explicit runtime the job names the leg.
        var jobStart = displayInfo.IndexOf(": ", StringComparison.Ordinal);
        var jobSegment = jobStart < 0 ? displayInfo : displayInfo[(jobStart + 2)..];
        var jobEnd = jobSegment.IndexOfAny(['(', '[']);
        return (jobEnd < 0 ? jobSegment : jobSegment[..jobEnd]).Trim();
    }
}
