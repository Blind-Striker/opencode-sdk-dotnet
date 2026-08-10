namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>A homogeneous array schema.</summary>
public sealed record ArrayNode : SchemaNode
{
    /// <summary>The schema for each array item.</summary>
    public required SchemaNode Item { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [Item];
}
