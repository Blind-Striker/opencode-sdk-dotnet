using System.Globalization;
using System.IO.Abstractions;
using OpenCode.Sdk.TestSupport.Ownership;
using OpenCode.Sdk.TestSupport.Ownership.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>Captures output and then failure metadata under independent finite deadlines.</summary>
internal sealed class ServerFailureCapture(
    ServerFailureArtifacts artifacts, IFileSystem fileSystem, IOwnedOperationDeadline deadline)
{
    private readonly List<LateCleanupFailureReport> _lateReports = [];

    public IReadOnlyList<LateCleanupFailureReport> LateReports => _lateReports;

    public async Task<Exception?> CaptureAsync(CliWrapServerAdapter? adapter, bool external, Exception? failure)
    {
        var capture = new OwnedCleanup(TimeSpan.FromSeconds(5), deadline);
        if (adapter is not null)
        {
            var snapshot = adapter.SnapshotLogs();
            capture.Own("pinned server stdout capture", _ => WriteLogAsync("stdout.log", snapshot.StandardOutput));
            capture.Own("pinned server stderr capture", _ => WriteLogAsync("stderr.log", snapshot.StandardError));
        }

        failure = await CaptureFailureAsync(capture, failure);
        var metadata = new OwnedCleanup(TimeSpan.FromSeconds(5), deadline);
        metadata.Own("pinned server failure metadata", _ =>
            artifacts.WriteMetadataAsync(external ? "external" : "owned",
                adapter?.ProcessId.ToString(CultureInfo.InvariantCulture) ?? "not-owned", failure));
        return await CaptureFailureAsync(metadata, failure);
    }

    private async Task WriteLogAsync(string name, IReadOnlyList<string> lines)
    {
        _ = fileSystem.Directory.CreateDirectory(artifacts.Directory);
        await new DiagnosticFileWriter(fileSystem).WriteAsync(
            fileSystem.Path.Combine(artifacts.Directory, name), string.Join(Environment.NewLine, lines));
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
