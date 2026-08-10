namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>A reference to another key in the flat schema graph.</summary>
public sealed record RefNode : SchemaNode
{
    /// <summary>The target schema-graph key.</summary>
    public required string Target { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [];
}
