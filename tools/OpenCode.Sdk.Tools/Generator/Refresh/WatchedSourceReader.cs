using System.IO.Abstractions;
using System.Text;
using OpenCode.Sdk.Tools.Generator.Refresh.Abstractions;
using OpenCode.Sdk.Tools.Generator.Refresh.Models;

namespace OpenCode.Sdk.Tools.Generator.Refresh;

/// <summary>
/// Observes the watched upstream sources at one revision of the submodule. Files are read as
/// git blobs (<c>git show &lt;revision&gt;:&lt;path&gt;</c>) so the observed hash is the same
/// object hash the pin was taken from, whatever the checkout's line endings. A watched file the
/// revision does not carry is a loud refusal: the door's input vanished.
/// </summary>
internal sealed class WatchedSourceReader(IFileSystem fileSystem, IProcessRunner processRunner)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<IReadOnlyList<ReceiptWatchedSource>> ObserveAsync(string revision, IReadOnlyList<WatchedSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentNullException.ThrowIfNull(sources);

        var submodule = _fileSystem.Path.GetFullPath(SnapshotPaths.Submodule);
        var observations = new List<ReceiptWatchedSource>(sources.Count);
        foreach (var source in sources)
        {
            var result = await _processRunner
                .RunAsync("git", ["show", $"{revision}:{source.Path}"], submodule, cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode is not 0)
            {
                var detail = result.StandardError.Trim();
                throw new SnapshotRefreshException(
                    $"watched source '{source.Path}' cannot be read at {revision}"
                    + $"{(detail.Length > 0 ? $": {detail}" : string.Empty)}");
            }

            observations.Add(new ReceiptWatchedSource
            {
                Path = source.Path,
                Sha256 = DocumentInspector.Sha256Hex(result.StandardOutput),
                AnchorMatched = Matches(source, result.StandardOutput),
            });
        }

        return Array.AsReadOnly([.. observations]);
    }

    private static bool Matches(WatchedSource source, byte[] content) =>
        string.Equals(source.Anchor.Type, SourceAnchor.Contains, StringComparison.Ordinal)
            ? Encoding.UTF8.GetString(content).Contains(source.Anchor.Text, StringComparison.Ordinal)
            : throw new SnapshotRefreshException(
                $"watched source '{source.Path}' declares unsupported anchor type '{source.Anchor.Type}'");
}
