using System.Text.Json;

namespace OpenCode.Sdk.Tools.Generator.Parsing;

internal sealed class SchemaNodeParser
{
    private const string ComponentSchemaPrefix = "#/components/schemas/";

    private readonly SpecParseErrorCollector _errors;
    private readonly SortedDictionary<string, SchemaNode> _graph;

    public SchemaNodeParser(SpecParseErrorCollector errors, SortedDictionary<string, SchemaNode> graph)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(graph);

        _errors = errors;
        _graph = graph;
    }

    public SchemaNode? Parse(JsonElement schema, string root, string pointer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(pointer);

        var location = BuildLocation(root, pointer);
        if (schema.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(location, "schema node must be an object");
            return null;
        }

        if (RefuseUnsupportedKeywords(schema, location))
        {
            return null;
        }

        if (schema.TryGetProperty("$ref", out var reference))
        {
            return ParseRef(schema, reference, location);
        }

        return ParseTyped(schema, root, pointer, location);
    }

    private static string BuildLocation(string root, string pointer) => pointer.Length == 0 ? $"schema '{root}'" : $"schema '{root}' at {pointer}";

    private static bool IsSupportedKeyword(string name) => name is "$ref" or "type" or "enum" or "items" or "description" or "format" or "pattern"
        or "minimum" or "exclusiveMinimum" or "maximum" or "minItems" or "maxItems";

    private bool RefuseUnsupportedKeywords(JsonElement schema, string location)
    {
        var refused = false;
        foreach (var propertyName in schema
                     .EnumerateObject()
                     .Select(static property => property.Name)
                     .Where(static propertyName => !IsSupportedKeyword(propertyName)))
        {
            _errors.Add(location, $"unsupported schema keyword '{propertyName}'");
            refused = true;
        }

        return refused;
    }

    private SchemaNode? ParseTyped(JsonElement schema, string root, string pointer, string location)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            _errors.Add(location, "schema must have a 'type' or '$ref'");
            return null;
        }

        if (type.ValueKind is JsonValueKind.Array)
        {
            _errors.Add(location, "type array is outside the dialect");
            return null;
        }

        if (type.ValueKind is not JsonValueKind.String)
        {
            _errors.Add(location, "schema type must be a string");
            return null;
        }

        return type.GetString() switch
        {
            "string" when schema.TryGetProperty("enum", out var values) => ParseEnum(schema, values, root, pointer, location),
            "string" => ParsePrimitive(schema, PrimitiveKind.String, location),
            "number" => ParsePrimitive(schema, PrimitiveKind.Number, location),
            "integer" => ParsePrimitive(schema, PrimitiveKind.Integer, location),
            "boolean" => ParsePrimitive(schema, PrimitiveKind.Boolean, location),
            "array" => ParseArray(schema, root, pointer, location),
            var typeName => RefuseType(typeName, location),
        };
    }

    private RefNode? ParseRef(JsonElement schema, JsonElement reference, string location)
    {
        if (HasRefSibling(schema))
        {
            _errors.Add(location, "$ref schemas must not have sibling keys");
            return null;
        }

        if (reference.ValueKind is not JsonValueKind.String)
        {
            _errors.Add(location, "$ref must be a string");
            return null;
        }

        var value = reference.GetString() ?? string.Empty;
        if (!value.StartsWith(ComponentSchemaPrefix, StringComparison.Ordinal) || value.Length == ComponentSchemaPrefix.Length)
        {
            _errors.Add(location, $"unsupported ref '{value}'");
            return null;
        }

        return new RefNode
        {
            Target = value[ComponentSchemaPrefix.Length..],
        };
    }

    private static bool HasRefSibling(JsonElement schema) => schema
        .EnumerateObject()
        .Any(static property => !string.Equals(property.Name, "$ref", StringComparison.Ordinal));

    private PrimitiveNode? ParsePrimitive(JsonElement schema, PrimitiveKind kind, string location)
    {
        if (schema.TryGetProperty("enum", out _))
        {
            _errors.Add(location, "enum is only handled for string schemas");
            return null;
        }

        if (schema.TryGetProperty("items", out _))
        {
            _errors.Add(location, "keyword 'items' is only valid for array schemas");
            return null;
        }

        if (!TryReadOptionalString(schema, "description", location, out var description)
            || !TryReadOptionalString(schema, "format", location, out var format))
        {
            return null;
        }

        return new PrimitiveNode
        {
            Kind = kind,
            Format = format,
            Description = description,
        };
    }

    private SchemaNode? ParseEnum(JsonElement schema, JsonElement valuesElement, string root, string pointer, string location)
    {
        if (schema.TryGetProperty("items", out _))
        {
            _errors.Add(location, "keyword 'items' is only valid for array schemas");
            return null;
        }

        if (valuesElement.ValueKind is not JsonValueKind.Array)
        {
            _errors.Add(location, "enum must be an array");
            return null;
        }

        List<string> values = [];
        foreach (var value in valuesElement.EnumerateArray())
        {
            if (value.ValueKind is not JsonValueKind.String)
            {
                _errors.Add(location, "non-string enum values are not yet handled");
                return null;
            }

            values.Add(value.GetString() ?? string.Empty);
        }

        if (values.Count < 2)
        {
            _errors.Add(location, "single-value enum not yet handled");
            return null;
        }

        if (!TryReadOptionalString(schema, "description", location, out var description)
            || !TryReadOptionalString(schema, "format", location, out _))
        {
            return null;
        }

        EnumNode node = new()
        {
            Values = [.. values],
            Description = description,
        };
        return Promote(node, root, pointer, location);
    }

    private ArrayNode? ParseArray(JsonElement schema, string root, string pointer, string location)
    {
        if (!schema.TryGetProperty("items", out var items))
        {
            _errors.Add(location, "array without items");
            return null;
        }

        if (schema.TryGetProperty("enum", out _))
        {
            _errors.Add(location, "keyword 'enum' is only valid for string schemas");
            return null;
        }

        if (!TryReadOptionalString(schema, "description", location, out var description))
        {
            return null;
        }

        var item = Parse(items, root, $"{pointer}/items");
        if (item is null)
        {
            return null;
        }

        return new ArrayNode
        {
            Item = item,
            Description = description,
        };
    }

    private SchemaNode? Promote(EnumNode node, string root, string pointer, string location)
    {
        if (pointer.Length == 0)
        {
            return node;
        }

        var key = $"{root}#{pointer}";
        if (!_graph.TryAdd(key, node))
        {
            _errors.Add(location, $"schema graph key collision '{key}'");
            return null;
        }

        return new RefNode
        {
            Target = key,
        };
    }

    private bool TryReadOptionalString(JsonElement schema, string name, string location, out string? value)
    {
        value = null;
        if (!schema.TryGetProperty(name, out var property))
        {
            return true;
        }

        if (property.ValueKind is not JsonValueKind.String)
        {
            _errors.Add(location, $"'{name}' must be a string");
            return false;
        }

        value = property.GetString();
        return true;
    }

    private SchemaNode? RefuseType(string? type, string location)
    {
        _errors.Add(location, $"schema type '{type}' is not yet handled");
        return null;
    }
}
