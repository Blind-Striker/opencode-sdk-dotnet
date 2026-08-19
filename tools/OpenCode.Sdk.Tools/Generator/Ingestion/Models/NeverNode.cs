namespace OpenCode.Sdk.Tools.Generator.Ingestion.Models;

/// <summary>Represents a schema that admits no JSON value.</summary>
public sealed record NeverNode : SchemaNode
{
    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [];
}
