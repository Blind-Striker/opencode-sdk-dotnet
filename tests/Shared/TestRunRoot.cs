using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>An isolated per-run directory under the OS temp root, deleted best-effort on dispose.</summary>
internal sealed class TestRunRoot : IDisposable
{
    private readonly IFileSystem _fileSystem;

    public TestRunRoot(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
        Path = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(), "opencode-sdk-tests", Guid.NewGuid().ToString("N"));
        _ = fileSystem.Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateSubdirectory(string name)
    {
        var directory = _fileSystem.Path.Combine(Path, name);
        _ = _fileSystem.Directory.CreateDirectory(directory);
        return directory;
    }

    public void Dispose()
    {
        try
        {
            _fileSystem.Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A straggling child handle wins; the OS temp cleaner owns the leftovers.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: retention is harmless, hanging a test run is not.
        }
    }
}
