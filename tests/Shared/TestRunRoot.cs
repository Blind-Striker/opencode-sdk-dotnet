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
        // Discarded deliberately: a straggling child handle winning the race is the one outcome
        // this root has no answer for, and retention is harmless where failing a whole test run
        // over a temp directory is not. The OS temp cleaner owns whatever is left.
        _ = BestEffortDelete.TryDeleteTree(_fileSystem, Path);
    }
}
