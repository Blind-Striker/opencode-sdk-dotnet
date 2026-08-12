using System.IO.Abstractions;
using OpenCode.Sdk.Tools.Output.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class RecordingProjectFormatter(
    IFileSystem fileSystem,
    Func<IFileSystem, string, IReadOnlyList<string>, CancellationToken, Task>? onFormat = null)
    : IProjectFormatter
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly Func<IFileSystem, string, IReadOnlyList<string>, CancellationToken, Task>? _onFormat = onFormat;

    public int CallCount { get; private set; }

    public string? ProjectPath { get; private set; }

    public IReadOnlyList<string> SourcePaths { get; private set; } = Array.AsReadOnly(Array.Empty<string>());

    public async Task FormatAsync(
        string projectPath,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(sourcePaths);
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        ProjectPath = projectPath;
        SourcePaths = Array.AsReadOnly([.. sourcePaths]);
        if (_onFormat is not null)
        {
            await _onFormat(_fileSystem, projectPath, sourcePaths, cancellationToken);
        }
    }
}
