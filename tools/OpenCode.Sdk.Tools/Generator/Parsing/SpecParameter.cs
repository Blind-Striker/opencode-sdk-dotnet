namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>A wire parameter declared by an operation.</summary>
public sealed record SpecParameter
{
    /// <summary>The parameter name exactly as authored in the spec.</summary>
    public required string Name { get; init; }

    /// <summary>The wire location from which the parameter is read.</summary>
    public required SpecParameterLocation Location { get; init; }

    /// <summary>The parameter value schema.</summary>
    public required SchemaNode Schema { get; init; }

    /// <summary>Whether the wire parameter is required.</summary>
    public required bool IsRequired { get; init; }

    /// <summary>Whether the parameter uses the supported deep-object serialization pair.</summary>
    public required bool IsDeepObject { get; init; }
}
