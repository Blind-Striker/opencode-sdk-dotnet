namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>A scalar string, number, integer, or boolean schema.</summary>
public sealed record PrimitiveNode : SchemaNode
{
    /// <summary>The scalar kind.</summary>
    public required PrimitiveKind Kind { get; init; }

    /// <summary>The OpenAPI format hint, when present.</summary>
    public string? Format { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children => [];
}
