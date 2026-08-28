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
        try
        {
            _fileSystem.Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the run root's own cleanup owns the leftovers.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}
