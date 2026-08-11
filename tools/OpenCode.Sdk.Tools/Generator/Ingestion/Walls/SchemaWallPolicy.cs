using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class SchemaWallPolicy
{
    private const string PrefixItemsAdmissionLocation = "Config/properties/plugin/items/anyOf/1";

    private readonly SchemaMemberWhitelist _memberWhitelist = new();

    public void Check(OpenApiSchema schema, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        _memberWhitelist.Check(schema, location, errors);

        if (schema.Type is { } type && ((int)type & ((int)type - 1)) != 0)
        {
            errors.Add(location, "schema keyword 'type' does not support multiple values");
        }

        if (schema.UnrecognizedKeywords is not null)
        {
            foreach (var keyword in schema
                         .UnrecognizedKeywords.Keys.Where(keyword => !IsAdmittedPrefixItems(keyword, location))
                         .Order(StringComparer.Ordinal))
            {
                errors.Add(location, $"unrecognized schema keyword '{keyword}' is not supported");
            }
        }

        if (schema.Extensions is null)
        {
            return;
        }

        foreach (var extension in schema.Extensions.Keys.Order(StringComparer.Ordinal))
        {
            errors.Add(location, $"schema-level extension '{extension}' is not supported");
        }
    }

    private static bool IsAdmittedPrefixItems(string keyword, string location) =>
        string.Equals(keyword, "prefixItems", StringComparison.Ordinal)
        && string.Equals(location, PrefixItemsAdmissionLocation, StringComparison.Ordinal);
}
