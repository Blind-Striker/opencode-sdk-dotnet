namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>An object schema whose arbitrary property values share one schema.</summary>
public sealed record DictionaryNode : SchemaNode
{
    /// <summary>The schema for every dictionary value.</summary>
    public required SchemaNode Value { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [Value];
}
