using System.IO.Abstractions;
using CliWrap;
using OpenCode.Sdk.Tools.Output.Abstractions;

namespace OpenCode.Sdk.Tools.Output;

internal sealed class CliWrapProjectFormatter(IFileSystem fileSystem) : IProjectFormatter
{
    /// <summary>
    /// Windows caps a child process's command line at 32,767 UTF-16 characters
    /// (<c>CreateProcessW</c>); the generated tree's file list alone crosses that once the
    /// profile grows large enough ("the filename or extension is too long" is
    /// <c>CreateProcessW</c>'s exact failure text). Staying well under the smallest platform's
    /// limit keeps every batch safe regardless of tree size.
    /// </summary>
    private const int MaxBatchCommandLineLength = 8_000;

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task FormatAsync(
        string projectPath,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(sourcePaths);
        if (sourcePaths.Count is 0)
        {
            return;
        }

        if (sourcePaths.Any(static sourcePath => string.IsNullOrWhiteSpace(sourcePath)))
        {
            throw new ArgumentException("Source paths cannot contain null or whitespace entries.", nameof(sourcePaths));
        }

        var fullProjectPath = _fileSystem.Path.GetFullPath(projectPath);
        var projectDirectory = _fileSystem.Path.GetDirectoryName(fullProjectPath)
                               ?? throw new InvalidOperationException($"Project path '{projectPath}' has no parent directory.");
        var projectFileName = _fileSystem.Path.GetFileName(fullProjectPath);

        foreach (var batch in Batch(sourcePaths, MaxBatchCommandLineLength))
        {
            _ = await Cli.Wrap("dotnet")
                .WithArguments(arguments => arguments
                    .Add("format")
                    .Add(projectFileName)
                    .Add("--no-restore")
                    .Add("--include")
                    .Add(batch))
                .WithWorkingDirectory(projectDirectory)
                .WithEnvironmentVariables(environment => environment.Set("TargetFramework", "net10.0"))
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Splits <paramref name="sourcePaths"/> into ordered groups whose paths, joined by single
    /// spaces, stay at or under <paramref name="maxLength"/> — one full path always ships even
    /// when it alone exceeds the budget, so no path is ever dropped.
    /// </summary>
    internal static IEnumerable<IReadOnlyList<string>> Batch(IReadOnlyList<string> sourcePaths, int maxLength)
    {
        var batch = new List<string>();
        var length = 0;
        foreach (var sourcePath in sourcePaths)
        {
            var addedLength = sourcePath.Length + 1;
            if (batch.Count > 0 && length + addedLength > maxLength)
            {
                yield return batch;
                batch = [];
                length = 0;
            }

            batch.Add(sourcePath);
            length += addedLength;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
