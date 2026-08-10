using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

namespace OpenCode.Sdk.Tools.Generator.Parsing.Operations;

/// <summary>The single request representation accepted by an operation.</summary>
public sealed record SpecRequestBody
{
    /// <summary>The request representation's media type.</summary>
    public required SpecMediaType ContentType { get; init; }

    /// <summary>The request payload schema.</summary>
    public required SchemaNode Schema { get; init; }

    /// <summary>Whether the request body is required.</summary>
    public required bool IsRequired { get; init; }
}
