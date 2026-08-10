namespace OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

/// <summary>A schema that additionally accepts JSON null.</summary>
public sealed record NullableNode : SchemaNode
{
    /// <summary>The non-null schema.</summary>
    public required SchemaNode Inner { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [Inner];
}
