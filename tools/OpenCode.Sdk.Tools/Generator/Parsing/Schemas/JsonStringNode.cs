namespace OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

/// <summary>A string whose content is encoded JSON matching an inner schema.</summary>
public sealed record JsonStringNode : SchemaNode
{
    /// <summary>The schema for the JSON content carried by the string.</summary>
    public required SchemaNode Inner { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [Inner];
}
