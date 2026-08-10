namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>A closed set of string values.</summary>
public sealed record EnumNode : SchemaNode
{
    /// <summary>The accepted wire values, in document order.</summary>
    public required IReadOnlyList<string> Values { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [];
}
