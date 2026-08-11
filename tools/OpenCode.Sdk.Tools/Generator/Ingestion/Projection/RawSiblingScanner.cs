using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal static class RawSiblingScanner
{
    private static readonly string[] SchemaChildKeywords = ["additionalProperties", "contentSchema", "items",];

    private static readonly string[] SchemaListKeywords = ["allOf", "anyOf", "oneOf", "prefixItems",];

    private static readonly string[] SchemaMapKeywords = ["patternProperties", "properties",];

    public static void Scan(JsonNode raw, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(errors);

        // The typed DOM proxies every member of a reference to its target, so a local
        // sibling key is invisible there; only the raw document can reveal it. Silently
        // proxying past a sibling would drop wire semantics, hence the scan refuses.

        if (raw is not JsonObject document)
        {
            return;
        }

        if (document["components"]?["schemas"] is JsonObject schemas)
        {
            foreach (var (name, schema) in schemas)
            {
                ScanSchema(schema, $"components/schemas/{name}", errors);
            }
        }

        if (document["paths"] is JsonObject paths)
        {
            foreach (var (path, pathItem) in paths)
            {
                ScanPathItem(pathItem, $"paths/{path}", errors);
            }
        }
    }

    private static void ScanPathItem(JsonNode? pathItem, string location, IngestionErrorCollector errors)
    {
        if (pathItem is not JsonObject operations)
        {
            return;
        }

        foreach (var (method, operation) in operations)
        {
            if (operation is not JsonObject concrete)
            {
                continue;
            }

            var operationLocation = $"{location}/{method}";
            if (concrete["parameters"] is JsonArray parameters)
            {
                for (var index = 0; index < parameters.Count; index++)
                {
                    ScanSchema(parameters[index]?["schema"],
                        string.Create(CultureInfo.InvariantCulture, $"{operationLocation}/parameters/{index}/schema"), errors);
                }
            }

            ScanContent(concrete["requestBody"], $"{operationLocation}/requestBody", errors);
            if (concrete["responses"] is not JsonObject responses)
            {
                continue;
            }

            foreach (var (status, response) in responses)
            {
                ScanContent(response, $"{operationLocation}/responses/{status}", errors);
            }
        }
    }

    private static void ScanContent(JsonNode? owner, string location, IngestionErrorCollector errors)
    {
        if (owner?["content"] is not JsonObject content)
        {
            return;
        }

        foreach (var (mediaType, media) in content)
        {
            ScanSchema(media?["schema"], $"{location}/content/{mediaType}/schema", errors);
        }
    }

    private static void ScanSchema(JsonNode? schema, string location, IngestionErrorCollector errors)
    {
        if (schema is not JsonObject concrete)
        {
            return;
        }

        if (concrete.ContainsKey("$ref"))
        {
            foreach (var (key, _) in concrete)
            {
                if (key is not ("$ref" or "description" or "summary"))
                {
                    errors.Add(location, $"'$ref' cannot carry sibling key '{key}'");
                }
            }

            return;
        }

        foreach (var keyword in SchemaChildKeywords)
        {
            ScanSchema(concrete[keyword], $"{location}/{keyword}", errors);
        }

        foreach (var keyword in SchemaListKeywords)
        {
            if (concrete[keyword] is not JsonArray branches)
            {
                continue;
            }

            for (var index = 0; index < branches.Count; index++)
            {
                ScanSchema(branches[index], string.Create(CultureInfo.InvariantCulture, $"{location}/{keyword}/{index}"), errors);
            }
        }

        // Property names are opaque wire data — only the values are schemas, so a
        // property literally named "$ref" never triggers the sibling rule.
        foreach (var keyword in SchemaMapKeywords)
        {
            if (concrete[keyword] is not JsonObject map)
            {
                continue;
            }

            foreach (var (name, value) in map)
            {
                ScanSchema(value, $"{location}/{keyword}/{name}", errors);
            }
        }
    }
}
