namespace OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

/// <summary>A named object property and its schema-graph node.</summary>
public sealed record SpecProperty
{
    /// <summary>The opaque wire property name.</summary>
    public required string Name { get; init; }

    /// <summary>The property schema.</summary>
    public required SchemaNode Schema { get; init; }

    /// <summary>Whether the property appears in the object's required set.</summary>
    public required bool IsRequired { get; init; }
}
