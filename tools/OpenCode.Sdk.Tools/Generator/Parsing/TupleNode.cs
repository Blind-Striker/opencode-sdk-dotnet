namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>A fixed-arity array schema with a distinct schema for each position.</summary>
public sealed record TupleNode : SchemaNode
{
    /// <summary>The item schemas in positional order.</summary>
    public required IReadOnlyList<SchemaNode> Items { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => Items;
}
