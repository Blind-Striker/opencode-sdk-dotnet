using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

/// <summary>Admits only the exact typed-applicator semantics exercised by the pinned dialect.</summary>
internal static class TypedApplicatorPolicy
{
    public static void Check(OpenApiSchema schema, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (schema.Not is null)
        {
            return;
        }

        var serialized = Serialize(schema);
        if (serialized is JsonObject { Count: 1 } host
            && host["not"] is JsonObject { Count: 0 })
        {
            return;
        }

        errors.Add(string.Concat(location, "/not"), "schema keyword 'not' is supported only as the standalone empty-schema form 'not: {}'");
    }

    private static JsonNode? Serialize(OpenApiSchema schema)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        var writer = new OpenApiJsonWriter(text);
        schema.SerializeAsV31(writer);
        return JsonNode.Parse(text.ToString());
    }
}
