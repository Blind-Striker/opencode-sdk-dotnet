namespace OpenCode.Sdk.TestSupport.Abstractions;

internal interface IGitProcess
{
    public Task RunAsync(string workingDirectory, string arguments, CancellationToken cancellationToken);
}
