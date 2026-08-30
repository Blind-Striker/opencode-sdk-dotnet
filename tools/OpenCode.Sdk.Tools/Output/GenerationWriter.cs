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

    /// <summary>Admitted family folders are single plain segments that never shadow a statically owned directory.</summary>
    private static readonly string[] StaticallyOwnedFolders = ["Models", "Internal", "Pagination", "Properties"];
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProjectFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));

    public async Task<WriteResult> WriteAsync(GenerationWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var outputRoot = request.OutputRoot;
        var familyFolders = ValidateFamilyFolders(request.FamilyFolders);
        var newSources = ValidateSources(outputRoot, request.Sources, familyFolders);
        var oldManifest = await LoadManifestAsync(outputRoot, cancellationToken).ConfigureAwait(false);
        var oldPaths = ValidateManifest(outputRoot, oldManifest);
        RefuseCaseOnlyPathChanges(oldPaths, newSources.Keys);
        RefuseUnmanifestedOverwrites(outputRoot, oldPaths, newSources.Keys);
        await RequireProvenanceOnManifestEntriesAsync(outputRoot, oldPaths, cancellationToken).ConfigureAwait(false);
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
        await WriteManifestAsync(outputRoot, newSources.Keys, request.ImplicitAliases, cancellationToken).ConfigureAwait(false);
        await WriteMarkerAsync(outputRoot, request.PartialMarkerContent, cancellationToken).ConfigureAwait(false);
        var sourcePaths = newSources.Keys.Order(StringComparer.Ordinal).ToArray();
        await _formatter.FormatAsync(request.ProjectPath, sourcePaths, cancellationToken).ConfigureAwait(false);

        var after = await SnapshotAsync(outputRoot, trackedPaths, cancellationToken).ConfigureAwait(false);
        return Compare(before, after, request.Verify);
    }

    private static HashSet<string> ValidateFamilyFolders(IReadOnlyList<string> familyFolders)
    {
        // Owned paths compare case-insensitively (Windows/macOS filesystems), so the
        // shadow and duplicate checks must too — 'models' collides with 'Models/' on disk.
        var result = new HashSet<string>(OwnedPathComparer);
        foreach (var folder in familyFolders)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder is "." or ".."
                || folder.Contains('/', StringComparison.Ordinal) || folder.Contains('\\', StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Family folder '{folder}' is unsafe.");
            }

            if (StaticallyOwnedFolders.Contains(folder, OwnedPathComparer))
            {
                throw new InvalidOperationException($"Family folder '{folder}' shadows a statically owned directory.");
            }

            if (!result.Add(folder))
            {
                throw new InvalidOperationException($"Family folder '{folder}' is duplicated.");
            }
        }

        return result;
    }

    private Dictionary<string, GeneratedSource> ValidateSources(string outputRoot, IReadOnlyList<GeneratedSource> sources,
        HashSet<string> familyFolders)
    {
        var result = new Dictionary<string, GeneratedSource>(OwnedPathComparer);
        foreach (var source in sources)
        {
            if (source is null)
            {
                throw new ArgumentException("Generated sources cannot contain null entries.", nameof(sources));
            }

            ValidateRelativePath(outputRoot, source.RelativePath);
            ValidateGeneratedSourcePath(source.RelativePath, familyFolders);

            // A headerless source would deadlock the next run's provenance requirement.
            if (!GenerationProvenance.HasHeader(Encoding.UTF8.GetString(source.Utf8Source.Span)))
            {
                throw new InvalidOperationException($"Generated source '{source.RelativePath}' lacks the provenance header.");
            }

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
            ValidateManifestSourcePath(relativePath);
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

    private async Task WriteManifestAsync(string outputRoot, IEnumerable<string> paths,
        IReadOnlyDictionary<string, string> implicitAliases, CancellationToken cancellationToken)
    {
        var manifest = new GenerationManifest
        {
            Files = [.. paths.Order(StringComparer.Ordinal)],
            ImplicitAliases = implicitAliases,
        };
        var json = $"{JsonSerializer.Serialize(manifest, ToolJsonContext.Default.GenerationManifest)}\n";
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

    /// <summary>New sources land in the root, a statically owned directory, or an admitted family folder — one level deep.</summary>
    private static void ValidateGeneratedSourcePath(string relativePath, HashSet<string> familyFolders)
    {
        if (!relativePath.EndsWith(".cs", StringComparison.Ordinal)
            || !(!relativePath.Contains('/', StringComparison.Ordinal)
                 || IsStaticallyOwnedPath(relativePath)
                 || IsAdmittedFamilyPath(relativePath, familyFolders)))
        {
            throw new InvalidOperationException($"Generated source path '{relativePath}' is outside the owned source directories.");
        }
    }

    /// <summary>
    /// Manifest entries validate shape, not family membership: a retired family's files must
    /// stay deletable, and the provenance requirement still guards every overwrite and delete.
    /// </summary>
    private static void ValidateManifestSourcePath(string relativePath)
    {
        if (!relativePath.EndsWith(".cs", StringComparison.Ordinal)
            || !(!relativePath.Contains('/', StringComparison.Ordinal)
                 || IsStaticallyOwnedPath(relativePath)
                 || relativePath.Split('/').Length is 2))
        {
            throw new InvalidOperationException($"Generation manifest path '{relativePath}' is outside the owned source directories.");
        }
    }

    private static bool IsStaticallyOwnedPath(string relativePath) =>
        relativePath.StartsWith("Models/", StringComparison.Ordinal)
        || relativePath.StartsWith("Internal/Serialization/", StringComparison.Ordinal)
        || relativePath.StartsWith("Internal/ResponseAdapters/", StringComparison.Ordinal)
        || relativePath.StartsWith("Internal/StreamAdapters/", StringComparison.Ordinal);

    private static bool IsAdmittedFamilyPath(string relativePath, HashSet<string> familyFolders) =>
        relativePath.Split('/') is [var folder, _] && familyFolders.Contains(folder);

    /// <summary>A new path that already exists on disk belongs to somebody else until the manifest owns it.</summary>
    private void RefuseUnmanifestedOverwrites(string outputRoot, HashSet<string> oldPaths, IEnumerable<string> newPaths)
    {
        var overwrite = newPaths
            .Where(path => !oldPaths.Contains(path))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(path => _fileSystem.File.Exists(ResolveOwnedPath(outputRoot, path)));
        if (overwrite is not null)
        {
            throw new InvalidOperationException($"Generated source path '{overwrite}' would overwrite an unmanifested file.");
        }
    }

    /// <summary>The writer only overwrites or deletes files that still carry the generated provenance header.</summary>
    private async Task RequireProvenanceOnManifestEntriesAsync(string outputRoot, IReadOnlySet<string> oldPaths,
        CancellationToken cancellationToken)
    {
        foreach (var relativePath in oldPaths.Order(StringComparer.Ordinal))
        {
            var path = ResolveOwnedPath(outputRoot, relativePath);
            if (!_fileSystem.File.Exists(path))
            {
                continue;
            }

            var content = await _fileSystem.File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (!GenerationProvenance.HasHeader(content))
            {
                throw new InvalidOperationException(
                    $"Manifest entry '{relativePath}' does not carry the provenance header; refusing to overwrite or delete it.");
            }
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
