using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Output.Abstractions;
using OpenCode.Sdk.Tools.Serialization;

namespace OpenCode.Sdk.Tools.Output;

internal sealed class GenerationWriter(IFileSystem fileSystem, IProjectFormatter formatter) : IGenerationWriter
{
    private const string ManifestRelativePath = ".generated-manifest.json";
    private const string MarkerRelativePath = ".generation-incomplete";
    private static readonly StringComparer OwnedPathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProjectFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));

    public async Task<WriteResult> WriteAsync(
        string outputRoot,
        string projectPath,
        IReadOnlyList<GeneratedSource> sources,
        string? partialMarkerContent,
        bool verify,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(sources);
        if (partialMarkerContent is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(partialMarkerContent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var newSources = ValidateSources(outputRoot, sources);
        var oldManifest = await LoadManifestAsync(outputRoot, cancellationToken).ConfigureAwait(false);
        var oldPaths = ValidateManifest(outputRoot, oldManifest);
        RefuseCaseOnlyPathChanges(oldPaths, newSources.Keys);
        var trackedPaths = oldPaths
            .Concat(newSources.Keys)
            .Append(ManifestRelativePath)
            .Append(MarkerRelativePath)
            .Distinct(OwnedPathComparer)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var before = await SnapshotAsync(outputRoot, trackedPaths, cancellationToken).ConfigureAwait(false);

        await WriteSourcesAsync(outputRoot, newSources, cancellationToken).ConfigureAwait(false);
        DeleteStaleSources(outputRoot, oldPaths, newSources.Keys);
        await WriteManifestAsync(outputRoot, newSources.Keys, cancellationToken).ConfigureAwait(false);
        await WriteMarkerAsync(outputRoot, partialMarkerContent, cancellationToken).ConfigureAwait(false);
        var sourcePaths = newSources.Keys.Order(StringComparer.Ordinal).ToArray();
        await _formatter.FormatAsync(projectPath, sourcePaths, cancellationToken).ConfigureAwait(false);

        var after = await SnapshotAsync(outputRoot, trackedPaths, cancellationToken).ConfigureAwait(false);
        return Compare(before, after, verify);
    }

    private Dictionary<string, GeneratedSource> ValidateSources(string outputRoot, IReadOnlyList<GeneratedSource> sources)
    {
        var result = new Dictionary<string, GeneratedSource>(OwnedPathComparer);
        foreach (var source in sources)
        {
            if (source is null)
            {
                throw new ArgumentException("Generated sources cannot contain null entries.", nameof(sources));
            }

            ValidateRelativePath(outputRoot, source.RelativePath);
            ValidateGeneratedSourcePath(source.RelativePath);

            if (!result.TryAdd(source.RelativePath, source))
            {
                throw new InvalidOperationException($"Generated source path '{source.RelativePath}' is duplicated.");
            }
        }

        return result;
    }

    private async Task<GenerationManifest> LoadManifestAsync(string outputRoot, CancellationToken cancellationToken)
    {
        var path = ResolveOwnedPath(outputRoot, ManifestRelativePath);
        if (!_fileSystem.File.Exists(path))
        {
            return new GenerationManifest { Files = [], };
        }

        try
        {
            var json = await _fileSystem.File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, ToolJsonContext.Default.GenerationManifest)
                   ?? throw new InvalidOperationException("The generation manifest cannot be JSON null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The generation manifest is invalid.", exception);
        }
    }

    private HashSet<string> ValidateManifest(string outputRoot, GenerationManifest manifest)
    {
        var result = new HashSet<string>(OwnedPathComparer);
        foreach (var relativePath in manifest.Files)
        {
            ValidateRelativePath(outputRoot, relativePath);
            ValidateGeneratedSourcePath(relativePath);
            if (!result.Add(relativePath))
            {
                throw new InvalidOperationException($"Generation manifest path '{relativePath}' is duplicated.");
            }
        }

        return result;
    }

    private async Task<Dictionary<string, byte[]?>> SnapshotAsync(string outputRoot, IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var relativePath in relativePaths)
        {
            var path = ResolveOwnedPath(outputRoot, relativePath);
            result.Add(
                relativePath,
                _fileSystem.File.Exists(path)
                    ? await _fileSystem.File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
                    : null);
        }

        return result;
    }

    private async Task WriteSourcesAsync(string outputRoot, Dictionary<string, GeneratedSource> sources,
        CancellationToken cancellationToken)
    {
        foreach (var (relativePath, source) in sources.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var path = ResolveOwnedPath(outputRoot, relativePath);
            EnsureParentDirectory(path);
            await _fileSystem.File.WriteAllBytesAsync(path, source.Utf8Source.ToArray(), cancellationToken).ConfigureAwait(false);
        }
    }

    private void DeleteStaleSources(string outputRoot, IReadOnlySet<string> oldPaths, IEnumerable<string> newPaths)
    {
        var retained = newPaths.ToHashSet(OwnedPathComparer);
        foreach (var relativePath in oldPaths.Where(path => !retained.Contains(path)).Order(StringComparer.Ordinal))
        {
            var path = ResolveOwnedPath(outputRoot, relativePath);
            if (_fileSystem.File.Exists(path))
            {
                _fileSystem.File.Delete(path);
            }
        }
    }

    private async Task WriteManifestAsync(string outputRoot, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var manifest = new GenerationManifest
        {
            Files = [.. paths.Order(StringComparer.Ordinal)],
        };
        var json = string.Concat(JsonSerializer.Serialize(manifest, ToolJsonContext.Default.GenerationManifest), "\n");
        await WriteBytesAsync(
            ResolveOwnedPath(outputRoot, ManifestRelativePath),
            Encoding.UTF8.GetBytes(json),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteMarkerAsync(string outputRoot, string? content, CancellationToken cancellationToken)
    {
        var path = ResolveOwnedPath(outputRoot, MarkerRelativePath);
        if (content is null)
        {
            if (_fileSystem.File.Exists(path))
            {
                _fileSystem.File.Delete(path);
            }

            return;
        }

        await WriteBytesAsync(path, Encoding.UTF8.GetBytes(content), cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        EnsureParentDirectory(path);
        await _fileSystem.File.WriteAllBytesAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureParentDirectory(string path)
    {
        var directory = _fileSystem.Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException($"Path '{path}' has no parent directory.");
        _fileSystem.Directory.CreateDirectory(directory);
    }

    private void ValidateRelativePath(string outputRoot, string relativePath)
    {
        _ = ResolveOwnedPath(outputRoot, relativePath);
    }

    private static void ValidateGeneratedSourcePath(string relativePath)
    {
        if (!relativePath.EndsWith(".cs", StringComparison.Ordinal)
            || !(relativePath.StartsWith("Models/", StringComparison.Ordinal)
                 || relativePath.StartsWith("Internal/Serialization/", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Generated source path '{relativePath}' is outside the owned source directories.");
        }
    }

    private string ResolveOwnedPath(string outputRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || _fileSystem.Path.IsPathRooted(relativePath)
            || relativePath.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Owned path '{relativePath}' is unsafe.");
        }

        var segments = relativePath.Split('/');
        if (segments.Any(static segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new InvalidOperationException($"Owned path '{relativePath}' is unsafe.");
        }

        var root = _fileSystem.Path.GetFullPath(outputRoot);
        var path = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine([root, .. segments]));
        var prefix = string.Concat(
            root.TrimEnd(_fileSystem.Path.DirectorySeparatorChar, _fileSystem.Path.AltDirectorySeparatorChar),
            _fileSystem.Path.DirectorySeparatorChar);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(prefix, pathComparison))
        {
            throw new InvalidOperationException($"Owned path '{relativePath}' escapes the output root.");
        }

        RefuseSymbolicLinks(root, segments, relativePath);

        return path;
    }

    private static void RefuseCaseOnlyPathChanges(HashSet<string> oldPaths, IEnumerable<string> newPaths)
    {
        foreach (var newPath in newPaths)
        {
            if (oldPaths.TryGetValue(newPath, out var oldPath)
                && !string.Equals(oldPath, newPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Generated source path '{newPath}' differs from owned path '{oldPath}' only by case.");
            }
        }
    }

    private void RefuseSymbolicLinks(string root, IEnumerable<string> segments, string relativePath)
    {
        var current = root;
        foreach (var segment in segments)
        {
            current = _fileSystem.Path.Combine(current, segment);
            if (_fileSystem.FileInfo.New(current).LinkTarget is not null)
            {
                throw new InvalidOperationException($"Owned path '{relativePath}' contains a symbolic link.");
            }
        }
    }

    private static WriteResult Compare(Dictionary<string, byte[]?> before, Dictionary<string, byte[]?> after, bool verify)
    {
        var created = new List<string>();
        var changed = new List<string>();
        var deleted = new List<string>();
        foreach (var relativePath in before.Keys.Order(StringComparer.Ordinal))
        {
            var oldContent = before[relativePath];
            var newContent = after[relativePath];
            if (oldContent is null && newContent is not null)
            {
                created.Add(relativePath);
            }
            else if (oldContent is not null && newContent is null)
            {
                deleted.Add(relativePath);
            }
            else if (oldContent is not null && newContent is not null && !oldContent.AsSpan().SequenceEqual(newContent))
            {
                changed.Add(relativePath);
            }
        }

        return new WriteResult
        {
            IsVerification = verify,
            CreatedPaths = created,
            ChangedPaths = changed,
            DeletedPaths = deleted,
        };
    }
}
