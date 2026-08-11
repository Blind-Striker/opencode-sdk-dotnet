namespace OpenCode.Sdk.Tools.Output.Abstractions;

internal interface IProjectFormatter
{
    public Task FormatAsync(
        string projectPath,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken);
}
