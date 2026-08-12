using OpenCode.Sdk.Tools.Generator.Emission;

namespace OpenCode.Sdk.Tools.Output.Abstractions;

internal interface IGenerationWriter
{
    public Task<WriteResult> WriteAsync(
        string outputRoot,
        string projectPath,
        IReadOnlyList<GeneratedSource> sources,
        string? partialMarkerContent,
        bool verify,
        CancellationToken cancellationToken);
}
