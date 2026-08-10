namespace OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

/// <summary>A schema accepting any one of multiple branches.</summary>
public sealed record UnionNode : SchemaNode
{
    /// <summary>The accepted branches in source document order.</summary>
    public required IReadOnlyList<SchemaNode> Branches { get; init; }

    /// <summary>The keyword that declared the union.</summary>
    public required UnionKeyword Keyword { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => Branches;
}
