namespace OpenCode.Sdk.Tools.Generator.Ingestion.Models;

/// <summary>Represents a projected homogeneous array schema.</summary>
public sealed record ArrayNode : SchemaNode
{
    /// <summary>Gets the projected item schema.</summary>
    public required SchemaNode Item { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [Item];
}
