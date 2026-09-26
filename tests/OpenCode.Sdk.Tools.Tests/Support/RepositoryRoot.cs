using System.IO.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Support;

/// <summary>
/// Locates the repository root — the directory holding <c>OpenCode.slnx</c> — above the test
/// output directory, for the tests that read committed repository files rather than fixtures.
/// </summary>
internal sealed class RepositoryRoot
{
    private const string SolutionFileName = "OpenCode.slnx";

    private readonly IFileSystem _fileSystem;

    public RepositoryRoot(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    public string Locate()
    {
        var current = _fileSystem.DirectoryInfo.New(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (_fileSystem.File.Exists(_fileSystem.Path.Combine(current.FullName, SolutionFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"The repository root ({SolutionFileName}) was not found above the test output directory.");
    }
}
