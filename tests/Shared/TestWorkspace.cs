using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>An isolated workspace directory under the fixture's per-run root (design §7.2).</summary>
public sealed class TestWorkspace : IDisposable
{
    private readonly IFileSystem _fileSystem;

    public TestWorkspace(IFileSystem fileSystem, string runRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
        Path = fileSystem.Path.Combine(runRoot, "workspaces", Guid.NewGuid().ToString("N"));
        _ = fileSystem.Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        // Discarded deliberately: this workspace lives under the run root, so a tree a straggling
        // child handle keeps alive here is swept again when that root is disposed.
        _ = BestEffortDelete.TryDeleteTree(_fileSystem, Path);
    }
}
