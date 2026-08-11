namespace OpenCode.Sdk.Tools.Generator.Ingestion.Models;

/// <summary>Identifies the upstream operation generation represented by an operation.</summary>
public enum SpecSurface
{
    /// <summary>The modern operation surface whose operation identifiers begin with <c>v2.</c>.</summary>
    Modern = 0,

    /// <summary>The legacy operation surface.</summary>
    Legacy = 1,
}
