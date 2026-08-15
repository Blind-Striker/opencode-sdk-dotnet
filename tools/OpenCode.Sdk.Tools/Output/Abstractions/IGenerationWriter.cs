namespace OpenCode.Sdk.Tools.Output.Abstractions;

internal interface IGenerationWriter
{
    public Task<WriteResult> WriteAsync(GenerationWriteRequest request, CancellationToken cancellationToken);
}
