using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

namespace OpenCode.Sdk.Tools.Generator.Parsing.Operations;

/// <summary>A single numeric response declared by an operation.</summary>
public sealed record SpecResponse
{
    /// <summary>The numeric HTTP response status.</summary>
    public required int StatusCode { get; init; }

    /// <summary>The optional response description.</summary>
    public string? Description { get; init; }

    /// <summary>The response representation's media type, or <see langword="null"/> for no content.</summary>
    public SpecMediaType? ContentType { get; init; }

    /// <summary>The response payload schema, or <see langword="null"/> for no content.</summary>
    public SchemaNode? Schema { get; init; }

    /// <summary>The structurally classified response envelope.</summary>
    public required SpecEnvelopeShape EnvelopeShape { get; init; }

    /// <summary>Whether the response representation is an event stream.</summary>
    public required bool IsSse { get; init; }

    /// <summary>Opaque event-stream metadata cloned from <c>x-effect-stream</c>, when declared.</summary>
    public JsonElement? EffectStreamMetadata { get; init; }
}
