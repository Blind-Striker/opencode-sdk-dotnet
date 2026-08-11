using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal static class SchemaWallPolicy
{
    private const string PrefixItemsAdmissionLocation = "Config/properties/plugin/items/anyOf/1";

    public static void Check(OpenApiSchema schema, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (schema.Type is { } type && ((int)type & ((int)type - 1)) != 0)
        {
            errors.Add(location, "schema keyword 'type' does not support multiple values");
        }

        // Unrecognized keywords are the one construct nothing downstream can ever see: the
        // reader retains them raw and every typed consumer is blind to them. Silence here
        // would be silent wire loss, so they refuse — unlike typed annotation/validation
        // members, which are known vocabulary and are deliberately ignored.
        if (schema.UnrecognizedKeywords is null)
        {
            return;
        }

        foreach (var keyword in schema
                     .UnrecognizedKeywords.Keys.Where(keyword => !IsAdmittedPrefixItems(keyword, location))
                     .Order(StringComparer.Ordinal))
        {
            errors.Add(location, $"unrecognized schema keyword '{keyword}' is not supported");
        }
    }

    private static bool IsAdmittedPrefixItems(string keyword, string location) =>
        string.Equals(keyword, "prefixItems", StringComparison.Ordinal)
        && string.Equals(location, PrefixItemsAdmissionLocation, StringComparison.Ordinal);
}
