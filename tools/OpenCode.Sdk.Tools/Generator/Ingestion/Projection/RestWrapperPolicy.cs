using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

/// <summary>
/// Recognizes Effect's <c>StructWithRest(Struct({…}), [Record(String, Any)])</c> encoding: the
/// struct's named properties sit on the host beside a single-element <c>allOf</c> whose element
/// carries only <c>type: object</c> and the rest's <c>additionalProperties</c>. That wrapper says
/// nothing the host's own <c>additionalProperties</c> could not say, so it reads as exactly that;
/// an <c>allOf</c> of any other shape keeps the classifier's refusal.
/// </summary>
internal static class RestWrapperPolicy
{
    /// <summary>The rest's value schema when the host carries the encoding, else null.</summary>
    public static IOpenApiSchema? TryGetRest(OpenApiSchema host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (host.Properties is null
            || host.AdditionalProperties is not null
            || !host.AdditionalPropertiesAllowed
            || host.PatternProperties is { Count: > 0 }
            || host.AllOf is not { Count: 1 }
            || host.AllOf[0] is not OpenApiSchema wrapper)
        {
            return null;
        }

        return IsRestOnly(wrapper) ? wrapper.AdditionalProperties : null;
    }

    /// <summary>
    /// Exactly the rest and nothing else: a typed object with a schema-valued
    /// <c>additionalProperties</c>, no structural, referencing, applicator, consumed, or
    /// unrecognized keyword beside it. Title and description are annotations the projection
    /// ignores everywhere, so they do not disqualify the wrapper.
    /// </summary>
    private static bool IsRestOnly(OpenApiSchema wrapper) =>
        wrapper is
        {
            Type: JsonSchemaType.Object,
            AdditionalProperties: not null,
            AdditionalPropertiesAllowed: true,
            Properties: null,
            Items: null,
            Enum: null,
            Const: null,
            Format: null,
            Not: null,
            If: null,
            Then: null,
            Else: null,
            Contains: null,
            PropertyNames: null,
            Discriminator: null,
            Xml: null,
            ExternalDocs: null,
            Default: null,
            Example: null,
            ContentEncoding: null,
            ContentMediaType: null,
            ContentSchema: null,
            Deprecated: false,
            ReadOnly: false,
            WriteOnly: false,
        }
        && wrapper.Required is not { Count: > 0 }
        && wrapper.AllOf is not { Count: > 0 }
        && wrapper.AnyOf is not { Count: > 0 }
        && wrapper.OneOf is not { Count: > 0 }
        && wrapper.PatternProperties is not { Count: > 0 }
        && wrapper.DependentSchemas is not { Count: > 0 }
        && wrapper.DependentRequired is not { Count: > 0 }
        && wrapper.Definitions is not { Count: > 0 }
        && wrapper.Examples is not { Count: > 0 }
        && wrapper.Vocabulary is not { Count: > 0 }
        && wrapper.Extensions is not { Count: > 0 }
        && wrapper.UnrecognizedKeywords is not { Count: > 0 };
}
