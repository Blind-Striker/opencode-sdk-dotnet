using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Locates the pinned server-under-test: bun running the submodule's CLI entry from source, the
/// only build in which the simulation package exists. Fail-fast by design (ADR-0022): a missing
/// submodule or dependency install is an instructive error, never a skip.
/// </summary>
internal sealed class PinnedServerCommand
{
    private readonly IFileSystem _fileSystem;
    private readonly Lazy<string> _repositoryRoot;

    public PinnedServerCommand(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
        _repositoryRoot = new Lazy<string>(FindRepositoryRoot);
    }

    public string RepositoryRoot => _repositoryRoot.Value;

    public IReadOnlyList<string> Resolve()
    {
        var submodule = _fileSystem.Path.Combine(RepositoryRoot, "external", "opencode");
        var entry = _fileSystem.Path.Combine(submodule, "packages", "cli", "src", "index.ts");
        if (!_fileSystem.File.Exists(entry))
        {
            throw new InvalidOperationException(
                $"The pinned server source is missing at '{entry}'. Run: git submodule update --init external/opencode");
        }

        if (!_fileSystem.Directory.Exists(_fileSystem.Path.Combine(submodule, "node_modules")))
        {
            throw new InvalidOperationException(
                $"The pinned server dependencies are not installed under '{submodule}'. Run there: bun install --frozen-lockfile --ignore-scripts");
        }

        return ["bun", entry, "serve"];
    }

    private string FindRepositoryRoot()
    {
        // The pattern (rather than a separate `!string.IsNullOrEmpty` call) is what narrows
        // `directory` to non-null on every TFM: net472's older BCL surface does not carry the
        // `NotNullWhen` attribute `IsNullOrEmpty` relies on for flow analysis elsewhere, which
        // would otherwise leave this a CS8604 on that leg only.
        var directory = AppContext.BaseDirectory;
        while (directory is { Length: > 0 })
        {
            if (_fileSystem.File.Exists(_fileSystem.Path.Combine(directory, "OpenCode.slnx")))
            {
                return directory;
            }

            directory = _fileSystem.Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (OpenCode.slnx) above the test base directory.");
    }
}
