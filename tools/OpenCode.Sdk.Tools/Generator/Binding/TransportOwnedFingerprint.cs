using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Computes a deterministic SHA-256 fingerprint over one ingested operation's wire subtree —
/// method, path, every parameter, the <c>x-websocket</c> marker, the request body, and declared
/// responses. <see cref="CurationValidator"/> uses it to pin a transport-owned operation
/// (ADR-0021): the operation is never selected into the generation profile, so a hand-written
/// door depends on its shape without the compiler ever seeing it, and a spec refresh that
/// reshapes it must fail generation loudly instead of drifting silently.
/// </summary>
/// <remarks>
/// Component schema references (<see cref="RefNode"/>) are identified by their target name only;
/// the referenced schema's own shape is not walked. Drift inside a referenced schema is the
/// reachable-schema and schema-alias checks' concern, not this fingerprint's — this fingerprint
/// answers one question only: has this operation's own declared surface changed. Parameter order
/// and object-property order are normalized before hashing (sorted by wire location/name) because
/// neither is wire-significant, so a byte-shuffle upstream that reorders them does not fire.
/// </remarks>
internal static class TransportOwnedFingerprint
{
    /// <summary>Computes the lowercase hex SHA-256 digest of <see cref="Canonicalize"/>'s output.</summary>
    public static string ComputeSha256(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var canonical = Canonicalize(operation);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Produces the deterministic, single-line canonical JSON text hashed by <see cref="ComputeSha256"/>.</summary>
    public static string Canonicalize(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var root = new JsonObject
        {
            ["method"] = operation.Method,
            ["path"] = operation.Path,
            ["websocket"] = operation.IsWebSocket,
            ["parameters"] = CanonicalizeParameters(operation.Parameters),
            ["requestBody"] = operation.RequestBody is { } requestBody ? CanonicalizeRequestBody(requestBody) : null,
            ["responses"] = CanonicalizeResponses(operation.Responses),
        };
        return root.ToJsonString();
    }

    private static JsonArray CanonicalizeParameters(IReadOnlyList<SpecParameter> parameters)
    {
        var array = new JsonArray();
        foreach (var parameter in parameters
                     .OrderBy(static parameter => parameter.Location)
                     .ThenBy(static parameter => parameter.Name, StringComparer.Ordinal))
        {
            array.Add(new JsonObject
            {
                ["name"] = parameter.Name,
                ["in"] = parameter.Location.ToString(),
                ["required"] = parameter.IsRequired,
                ["deepObject"] = parameter.IsDeepObject,
                ["schema"] = CanonicalizeSchema(parameter.Schema),
            });
        }

        return array;
    }

    private static JsonObject CanonicalizeRequestBody(SpecRequestBody requestBody) =>
        new()
        {
            ["contentType"] = requestBody.ContentType.Stripped,
            ["required"] = requestBody.IsRequired,
            ["schema"] = CanonicalizeSchema(requestBody.Schema),
        };

    private static JsonArray CanonicalizeResponses(IReadOnlyList<SpecResponse> responses)
    {
        var array = new JsonArray();
        foreach (var response in responses.OrderBy(static response => response.StatusCode))
        {
            array.Add(new JsonObject
            {
                ["status"] = response.StatusCode,
                ["contentType"] = response.ContentType?.Stripped,
                ["envelope"] = response.EnvelopeShape.ToString(),
                ["sse"] = response.IsSse,
                ["schema"] = response.Schema is { } schema ? CanonicalizeSchema(schema) : null,
                ["effectStream"] = response.EffectStream is { } stream ? CanonicalizeEffectStream(stream) : null,
            });
        }

        return array;
    }

    private static JsonObject CanonicalizeEffectStream(SpecEffectStreamContract stream) =>
        new()
        {
            ["encoding"] = stream.Encoding,
            ["failureEventName"] = stream.FailureEventName,
            ["causeSchema"] = stream.CauseSchema is { } cause ? CanonicalizeSchema(cause) : null,
            ["errorSchema"] = stream.ErrorSchema is { } error ? CanonicalizeSchema(error) : null,
        };

    private static JsonNode CanonicalizeSchema(SchemaNode node) => node switch
    {
        RefNode reference => new JsonObject { ["kind"] = "ref", ["target"] = reference.Target, ["format"] = node.Format },
        PrimitiveNode primitive => new JsonObject { ["kind"] = "primitive", ["primitiveKind"] = primitive.Kind.ToString(), ["format"] = node.Format },
        LiteralNode literal => new JsonObject
        {
            ["kind"] = "literal",
            ["literalKind"] = literal.Kind.ToString(),
            ["dialect"] = literal.Dialect.ToString(),
            ["value"] = literal.Value,
            ["format"] = node.Format,
        },
        EnumNode @enum => new JsonObject
        {
            ["kind"] = "enum",
            ["values"] = new JsonArray([.. @enum.Values.Order(StringComparer.Ordinal).Select(static value => (JsonNode)value)]),
            ["format"] = node.Format,
        },
        NullableNode nullable => new JsonObject { ["kind"] = "nullable", ["inner"] = CanonicalizeSchema(nullable.Inner), ["format"] = node.Format },
        ArrayNode array => new JsonObject { ["kind"] = "array", ["item"] = CanonicalizeSchema(array.Item), ["format"] = node.Format },
        TupleNode tuple => new JsonObject
        {
            ["kind"] = "tuple",
            ["items"] = new JsonArray([.. tuple.Items.Select(CanonicalizeSchema)]),
            ["format"] = node.Format,
        },
        DictionaryNode dictionary => new JsonObject { ["kind"] = "dictionary", ["value"] = CanonicalizeSchema(dictionary.Value), ["format"] = node.Format },
        UnionNode union => new JsonObject
        {
            ["kind"] = "union",
            ["keyword"] = union.Keyword.ToString(),
            ["classification"] = union.Classification.ToString(),
            ["branches"] = new JsonArray([.. union.Branches.Select(CanonicalizeSchema)]),
            ["format"] = node.Format,
        },
        ObjectNode @object => CanonicalizeObject(@object),
        FreeFormObjectNode => new JsonObject { ["kind"] = "freeFormObject", ["format"] = node.Format },
        UnrestrictedNode => new JsonObject { ["kind"] = "unrestricted", ["format"] = node.Format },
        NeverNode => new JsonObject { ["kind"] = "never", ["format"] = node.Format },
        EncodedStringNode encoded => new JsonObject { ["kind"] = "encodedString", ["contentEncoding"] = encoded.ContentEncoding, ["format"] = node.Format },
        JsonStringNode jsonString => new JsonObject { ["kind"] = "jsonString", ["inner"] = CanonicalizeSchema(jsonString.Inner), ["format"] = node.Format },
        SpecialNumberNode => new JsonObject { ["kind"] = "specialNumber", ["format"] = node.Format },
        _ => throw new InvalidOperationException($"Unreachable schema node kind '{node.GetType().Name}'."),
    };

    private static JsonObject CanonicalizeObject(ObjectNode node)
    {
        var properties = new JsonArray();
        foreach (var property in node.Properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            properties.Add(new JsonObject
            {
                ["name"] = property.Name,
                ["required"] = property.IsRequired,
                ["schema"] = CanonicalizeSchema(property.Schema),
            });
        }

        var literalMarkers = new JsonArray();
        foreach (var marker in node.LiteralMarkers.OrderBy(static marker => marker.PropertyName, StringComparer.Ordinal))
        {
            literalMarkers.Add(new JsonObject
            {
                ["propertyName"] = marker.PropertyName,
                ["kind"] = marker.Kind.ToString(),
                ["value"] = marker.Value,
            });
        }

        return new JsonObject
        {
            ["kind"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = node.AdditionalProperties.ToString(),
            ["additionalPropertiesSchema"] = node.AdditionalPropertiesSchema is { } additional ? CanonicalizeSchema(additional) : null,
            ["literalMarkers"] = literalMarkers,
            ["errorStyle"] = node.ErrorStyle.ToString(),
            ["format"] = node.Format,
        };
    }
}
