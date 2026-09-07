using System.Globalization;
using System.IO.Abstractions;
using OpenCode.Sdk.TestSupport.Ownership;
using OpenCode.Sdk.TestSupport.Ownership.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>Captures output and then failure metadata under independent finite deadlines.</summary>
internal sealed class ServerFailureCapture(
    ServerFailureArtifacts artifacts, IFileSystem fileSystem, IOwnedOperationDeadline deadline)
{
    private const string TruncatedHeader = "[truncated: older lines, or part of an oversized line, were discarded to stay inside the retention bounds]";

    private readonly List<LateCleanupFailureReport> _lateReports = [];

    public IReadOnlyList<LateCleanupFailureReport> LateReports => _lateReports;

    /// <summary>
    /// Writes the collector's final snapshot (when a server was owned) and the failure metadata.
    /// The snapshot is taken here, after the owned server's disposal has settled, so it is the
    /// launcher's final collection rather than a mid-shutdown view.
    /// </summary>
    public async Task<Exception?> CaptureAsync(OpenCodeServerOutput? output, int? processId, bool external,
        Exception? failure, IReadOnlyList<Exception> teardownFailures)
    {
        var capture = new OwnedCleanup(TimeSpan.FromSeconds(5), deadline);
        if (output is not null)
        {
            var snapshot = output.GetSnapshot();
            capture.Own("pinned server stdout capture",
                _ => WriteLogAsync("stdout.log", snapshot.StandardOutput, snapshot.StandardOutputTruncated));
            capture.Own("pinned server stderr capture",
                _ => WriteLogAsync("stderr.log", snapshot.StandardError, snapshot.StandardErrorTruncated));
        }

        failure = await CaptureFailureAsync(capture, failure);
        if (failure is not null && artifacts.Failures.Count is 0)
        {
            _ = artifacts.Mark(failure, "fixture disposal", "phase=server diagnostic capture");
        }

        var lifecycleFailures = teardownFailures.Concat(capture.OperationFailures).ToArray();
        var metadata = new OwnedCleanup(TimeSpan.FromSeconds(5), deadline);
        metadata.Own("pinned server failure metadata", _ =>
            artifacts.WriteMetadataAsync(external ? "external" : "owned",
                processId?.ToString(CultureInfo.InvariantCulture) ?? "not-owned", lifecycleFailures));
        return await CaptureFailureAsync(metadata, failure);
    }

    private async Task WriteLogAsync(string name, IReadOnlyList<string> lines, bool truncated)
    {
        _ = fileSystem.Directory.CreateDirectory(artifacts.Directory);
        var content = truncated ? lines.Prepend(TruncatedHeader) : lines;
        await new DiagnosticFileWriter(fileSystem).WriteAsync(
            fileSystem.Path.Combine(artifacts.Directory, name), string.Join(Environment.NewLine, content));
    }

    private async Task<Exception?> CaptureFailureAsync(OwnedCleanup cleanup, Exception? primary)
    {
        try
        {
            await cleanup.CompleteAsync(primary);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
        finally
        {
            if (cleanup.LateFailures is { } late)
            {
                _lateReports.Add(late);
            }
        }
    }
}
