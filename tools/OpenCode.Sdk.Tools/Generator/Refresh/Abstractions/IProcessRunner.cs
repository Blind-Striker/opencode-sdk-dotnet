namespace OpenCode.Sdk.Tools.Generator.Refresh.Abstractions;

/// <summary>Runs an external process and captures its output; the synchronizer's only process seam.</summary>
public interface IProcessRunner
{
    /// <summary>Runs the process to completion, capturing stdout bytes and stderr text; never throws on a nonzero exit.</summary>
    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory,
        CancellationToken cancellationToken);
}
