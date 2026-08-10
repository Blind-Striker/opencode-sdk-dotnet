namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>An object schema with a named property bag.</summary>
public sealed record ObjectNode : SchemaNode
{
    /// <summary>The named properties in source document order.</summary>
    public required IReadOnlyList<SpecProperty> Properties { get; init; }

    /// <summary>Required literal-valued properties in source document order.</summary>
    public required IReadOnlyList<LiteralMarker> LiteralMarkers { get; init; }

    /// <summary>How properties outside <see cref="Properties"/> are handled.</summary>
    public required AdditionalPropertiesKind AdditionalProperties { get; init; }

    /// <summary>The schema for additional properties when <see cref="AdditionalProperties"/> is <see cref="AdditionalPropertiesKind.Schema"/>.</summary>
    public SchemaNode? AdditionalPropertiesSchema { get; init; }

    /// <inheritdoc />
    public override IEnumerable<SchemaNode> Children =>
        AdditionalPropertiesSchema is { } additionalPropertiesSchema
            ? Properties
                .Select(static property => property.Schema)
                .Append(additionalPropertiesSchema)
            : Properties.Select(static property => property.Schema);
}
