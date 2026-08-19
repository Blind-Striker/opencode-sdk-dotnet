using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal static class SchemaWallPolicy
{
    public static void Check(OpenApiSchema schema, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (schema.Type is { } type && ((int)type & ((int)type - 1)) != 0)
        {
            errors.Add(location, "schema keyword 'type' does not support multiple values");
        }

        TypedApplicatorPolicy.Check(schema, location, errors);

        // Unrecognized keywords are the one construct nothing downstream can ever see: the
        // reader retains them raw and every typed consumer is blind to them. Silence here
        // would be silent wire loss, so they refuse — unlike typed annotation/validation
        // members, which are known vocabulary and are deliberately ignored. The single
        // exception is prefixItems: the classifier routes it and the adapter admits exactly
        // two shapes (closed tuple, non-empty homogeneous array), refusing the rest located.
        if (schema.UnrecognizedKeywords is null)
        {
            return;
        }

        foreach (var keyword in schema
                     .UnrecognizedKeywords.Keys.Where(static keyword => !string.Equals(keyword, "prefixItems", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            errors.Add(location, $"unrecognized schema keyword '{keyword}' is not supported");
        }
    }
}
