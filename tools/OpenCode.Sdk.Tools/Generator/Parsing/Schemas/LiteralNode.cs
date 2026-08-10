namespace OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

/// <summary>A single accepted string or boolean literal.</summary>
public sealed record LiteralNode : SchemaNode
{
    /// <summary>The literal's primitive kind.</summary>
    public required LiteralKind Kind { get; init; }

    /// <summary>The literal value in its JSON scalar spelling.</summary>
    public required string Value { get; init; }

    /// <summary>The schema dialect that declared the literal.</summary>
    public required LiteralDialect Dialect { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [];
}
