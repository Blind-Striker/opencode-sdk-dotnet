namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>An unconstrained object schema with no declared property bag.</summary>
public sealed record FreeFormObjectNode : SchemaNode
{
    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [];
}
