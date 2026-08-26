using OpenCode.Sdk.Tools.Generator.Refresh.Models;

namespace OpenCode.Sdk.Tools.Generator.Refresh;

/// <summary>A validated patch manifest paired with the on-disk patch it governs.</summary>
internal sealed record LoadedPatch
{
    public required string ManifestName { get; init; }

    public required PatchManifest Manifest { get; init; }

    public required string PatchPath { get; init; }
}
