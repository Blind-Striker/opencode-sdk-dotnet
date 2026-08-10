namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>A JSON number that also accepts named non-finite floating-point string values.</summary>
public sealed record SpecialNumberNode : SchemaNode
{
    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [];
}
