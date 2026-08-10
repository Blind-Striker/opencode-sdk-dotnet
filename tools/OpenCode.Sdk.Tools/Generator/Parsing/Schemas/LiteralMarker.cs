namespace OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

/// <summary>A required literal-valued property that can identify an object branch.</summary>
public sealed record LiteralMarker
{
    /// <summary>The opaque wire property name.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The marker literal's primitive kind.</summary>
    public required LiteralKind Kind { get; init; }

    /// <summary>The marker literal's JSON scalar spelling.</summary>
    public required string Value { get; init; }
}
