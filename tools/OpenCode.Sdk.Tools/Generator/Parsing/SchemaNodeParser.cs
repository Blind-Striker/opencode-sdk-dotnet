using System.Globalization;
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

        var hasAnyOf = schema.TryGetProperty("anyOf", out var anyOf);
        var hasOneOf = schema.TryGetProperty("oneOf", out var oneOf);
        if (hasAnyOf && hasOneOf)
        {
            _errors.Add(location, "anyOf and oneOf cannot be combined");
            return null;
        }

        if (schema.TryGetProperty("$ref", out var reference))
        {
            return ParseRef(schema, reference, location);
        }

        if (hasAnyOf)
        {
            return ParseUnion(schema, anyOf, UnionKeyword.AnyOf, root, pointer, location);
        }

        if (hasOneOf)
        {
            return ParseUnion(schema, oneOf, UnionKeyword.OneOf, root, pointer, location);
        }

        if (schema.TryGetProperty("const", out var constValue))
        {
            return ParseConst(schema, constValue, location);
        }

        return ParseTyped(schema, root, pointer, location);
    }

    private static string BuildLocation(string root, string pointer) => pointer.Length == 0 ? $"schema '{root}'" : $"schema '{root}' at {pointer}";

    private static bool IsSupportedKeyword(string name) => name is "$ref" or "type" or "enum" or "const" or "anyOf" or "oneOf"
        or "items" or "prefixItems" or "properties" or "required" or "additionalProperties" or "patternProperties" or "description"
        or "format" or "contentSchema" or "contentMediaType" or "pattern" or "minimum" or "exclusiveMinimum" or "maximum"
        or "minItems" or "maxItems";

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

        var typeName = type.GetString();
        if (RefuseKeywordsOutsideType(schema, typeName, location))
        {
            return null;
        }

        return typeName switch
        {
            "string" => ParseString(schema, root, pointer, location),
            "number" => ParsePrimitive(schema, PrimitiveKind.Number, location),
            "integer" => ParsePrimitive(schema, PrimitiveKind.Integer, location),
            "boolean" when schema.TryGetProperty("enum", out var values) => ParseBooleanEnum(schema, values, location),
            "boolean" => ParsePrimitive(schema, PrimitiveKind.Boolean, location),
            "object" => ParseObject(schema, root, pointer, location),
            "array" => ParseArray(schema, root, pointer, location),
            "null" => RefuseNullType(location),
            _ => RefuseType(typeName, location),
        };
    }

    private SchemaNode? ParseString(JsonElement schema, string root, string pointer, string location)
    {
        if (schema.TryGetProperty("contentSchema", out _) || schema.TryGetProperty("contentMediaType", out _))
        {
            return ParseJsonString(schema, root, pointer, location);
        }

        return schema.TryGetProperty("enum", out var values)
            ? ParseEnum(schema, values, root, pointer, location)
            : ParsePrimitive(schema, PrimitiveKind.String, location);
    }

    private JsonStringNode? ParseJsonString(JsonElement schema, string root, string pointer, string location)
    {
        if (RefuseObjectOnlyKeywords(schema, location)
            || RefuseItemsKeyword(schema, location))
        {
            return null;
        }

        if (schema.TryGetProperty("enum", out _))
        {
            _errors.Add(location, "keyword 'enum' cannot be combined with contentSchema");
            return null;
        }

        if (!schema.TryGetProperty("contentSchema", out var contentSchema))
        {
            _errors.Add(location, "contentSchema must be present when contentMediaType is present");
            return null;
        }

        if (!schema.TryGetProperty("contentMediaType", out var contentMediaType)
            || contentMediaType.ValueKind is not JsonValueKind.String
            || !string.Equals(contentMediaType.GetString(), "application/json", StringComparison.Ordinal))
        {
            _errors.Add(location, "contentMediaType must be 'application/json'");
            return null;
        }

        if (!TryReadOptionalString(schema, "description", location, out var description)
            || !TryReadOptionalString(schema, "format", location, out _))
        {
            return null;
        }

        var inner = Parse(contentSchema, root, $"{pointer}/contentSchema");
        return inner is null
            ? null
            : new JsonStringNode
            {
                Inner = inner,
                Description = description,
            };
    }

    private bool RefuseKeywordsOutsideType(JsonElement schema, string? typeName, string location)
    {
        var refused = false;
        if (typeName is not "array" && RefuseKeywordOutsideType(schema, "prefixItems", "array", location))
        {
            refused = true;
        }

        if (typeName is not "string" && RefuseKeywordOutsideType(schema, "contentSchema", "string", location))
        {
            refused = true;
        }

        if (typeName is not "string" && RefuseKeywordOutsideType(schema, "contentMediaType", "string", location))
        {
            refused = true;
        }

        return refused;
    }

    private bool RefuseKeywordOutsideType(JsonElement schema, string keyword, string typeName, string location)
    {
        if (!schema.TryGetProperty(keyword, out _))
        {
            return false;
        }

        _errors.Add(location, $"keyword '{keyword}' is only valid for {typeName} schemas");
        return true;
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
        if (RefuseObjectOnlyKeywords(schema, location))
        {
            return null;
        }

        if (schema.TryGetProperty("enum", out _))
        {
            _errors.Add(location, "enum is only handled for string and boolean schemas");
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
        if (RefuseObjectOnlyKeywords(schema, location))
        {
            return null;
        }

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

        if (values.Count == 0)
        {
            _errors.Add(location, "enum must contain at least one value");
            return null;
        }

        if (!TryReadOptionalString(schema, "description", location, out var description)
            || !TryReadOptionalString(schema, "format", location, out _))
        {
            return null;
        }

        if (values.Count == 1)
        {
            return new LiteralNode
            {
                Kind = LiteralKind.String,
                Value = values[0],
                Dialect = LiteralDialect.SingleValueEnum,
                Description = description,
            };
        }

        EnumNode node = new()
        {
            Values = [.. values],
            Description = description,
        };
        return Promote(node, root, pointer, location);
    }

    private LiteralNode? ParseBooleanEnum(JsonElement schema, JsonElement valuesElement, string location)
    {
        if (RefuseObjectOnlyKeywords(schema, location)
            || RefuseItemsKeyword(schema, location))
        {
            return null;
        }

        if (valuesElement.ValueKind is not JsonValueKind.Array)
        {
            _errors.Add(location, "enum must be an array");
            return null;
        }

        var values = valuesElement.EnumerateArray();
        if (!values.MoveNext())
        {
            _errors.Add(location, "enum must contain at least one value");
            return null;
        }

        var value = values.Current;
        if (values.MoveNext())
        {
            _errors.Add(location, "boolean enum must contain exactly one value");
            return null;
        }

        if (!TryReadLiteralValue(value, out var kind, out var text) || kind is not LiteralKind.Boolean)
        {
            _errors.Add(location, "boolean enum value must be a boolean");
            return null;
        }

        if (!TryReadOptionalString(schema, "description", location, out var description)
            || !TryReadOptionalString(schema, "format", location, out _))
        {
            return null;
        }

        return new LiteralNode
        {
            Kind = kind,
            Value = text,
            Dialect = LiteralDialect.SingleValueEnum,
            Description = description,
        };
    }

    private bool RefuseKeywordWithConst(JsonElement schema, string keyword, string location)
    {
        if (!schema.TryGetProperty(keyword, out _))
        {
            return false;
        }

        _errors.Add(location, $"keyword '{keyword}' cannot be combined with const");
        return true;
    }

    private LiteralNode? ParseConst(JsonElement schema, JsonElement valueElement, string location)
    {
        if (schema.TryGetProperty("enum", out _))
        {
            _errors.Add(location, "enum and const cannot be combined");
            return null;
        }

        if (RefuseObjectOnlyKeywords(schema, location)
            || RefuseItemsKeyword(schema, location)
            || RefuseKeywordWithConst(schema, "prefixItems", location)
            || RefuseKeywordWithConst(schema, "contentSchema", location)
            || RefuseKeywordWithConst(schema, "contentMediaType", location))
        {
            return null;
        }

        if (!TryReadLiteralValue(valueElement, out var kind, out var value))
        {
            _errors.Add(location, "const must be a string or boolean");
            return null;
        }

        if (!ValidateLiteralType(schema, kind, location)
            || !TryReadOptionalString(schema, "description", location, out var description)
            || !TryReadOptionalString(schema, "format", location, out _))
        {
            return null;
        }

        return new LiteralNode
        {
            Kind = kind,
            Value = value,
            Dialect = LiteralDialect.Const,
            Description = description,
        };
    }

    private SchemaNode? ParseUnion(JsonElement schema,
        JsonElement branchesElement,
        UnionKeyword keyword,
        string root,
        string pointer,
        string location)
    {
        var keywordName = keyword is UnionKeyword.AnyOf ? "anyOf" : "oneOf";
        if (RefuseUnionSiblings(schema, keywordName, location)
            || !TryReadOptionalString(schema, "description", location, out var description))
        {
            return null;
        }

        if (branchesElement.ValueKind is not JsonValueKind.Array)
        {
            _errors.Add(location, $"{keywordName} must be an array");
            return null;
        }

        if (keyword is UnionKeyword.AnyOf && IsSpecialNumberUnion(branchesElement))
        {
            return new SpecialNumberNode
            {
                Description = description,
            };
        }

        var deduplicatedBranches = DeduplicateRawRefBranches(branchesElement);
        var nonNullBranches = ExtractNullBranches(deduplicatedBranches, out var isNullable);
        if (nonNullBranches.Count == 0)
        {
            _errors.Add(location, $"{keywordName} must contain at least one non-null branch");
            return null;
        }

        var parsedBranches = ParseUnionBranches(nonNullBranches, root, pointer, keywordName);
        if (parsedBranches is null)
        {
            return null;
        }

        var unionDescription = isNullable ? null : description;
        var inner = parsedBranches.Count == 1
            ? parsedBranches[0]
            : PromoteUnion(parsedBranches, keyword, root, pointer, location, unionDescription);
        return inner is null ? null : WrapNullable(inner, isNullable, description);
    }

    private static bool IsSpecialNumberUnion(JsonElement branchesElement)
    {
        var numberBranchCount = 0;
        var stringEnumBranchCount = 0;
        foreach (var branch in branchesElement.EnumerateArray())
        {
            if (IsExactNumberBranch(branch))
            {
                numberBranchCount++;
                continue;
            }

            if (!IsSpecialNumberStringBranch(branch))
            {
                return false;
            }

            stringEnumBranchCount++;
        }

        return numberBranchCount == 1 && stringEnumBranchCount > 0;
    }

    private static bool IsExactNumberBranch(JsonElement branch)
    {
        if (branch.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var properties = branch.EnumerateObject();
        if (!properties.MoveNext())
        {
            return false;
        }

        var type = properties.Current;
        return !properties.MoveNext()
               && string.Equals(type.Name, "type", StringComparison.Ordinal)
               && type.Value.ValueKind is JsonValueKind.String
               && string.Equals(type.Value.GetString(), "number", StringComparison.Ordinal);
    }

    private static bool IsSpecialNumberStringBranch(JsonElement branch)
    {
        if (!TryReadExactStringEnumBranch(branch, out var values))
        {
            return false;
        }

        var hasValue = false;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind is not JsonValueKind.String
                || !IsSpecialNumberValue(value.GetString()))
            {
                return false;
            }

            hasValue = true;
        }

        return hasValue;
    }

    private static bool TryReadExactStringEnumBranch(JsonElement branch, out JsonElement values)
    {
        values = default;
        if (branch.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var hasType = false;
        var hasEnum = false;
        foreach (var property in branch.EnumerateObject())
        {
            if (string.Equals(property.Name, "type", StringComparison.Ordinal))
            {
                if (hasType
                    || property.Value.ValueKind is not JsonValueKind.String
                    || !string.Equals(property.Value.GetString(), "string", StringComparison.Ordinal))
                {
                    return false;
                }

                hasType = true;
                continue;
            }

            if (!string.Equals(property.Name, "enum", StringComparison.Ordinal)
                || hasEnum
                || property.Value.ValueKind is not JsonValueKind.Array)
            {
                return false;
            }

            hasEnum = true;
            values = property.Value;
        }

        return hasType && hasEnum;
    }

    private static bool IsSpecialNumberValue(string? value) => value is "NaN" or "Infinity" or "-Infinity";

    private List<SchemaNode>? ParseUnionBranches(IReadOnlyList<(JsonElement Element, int Ordinal)> branches,
        string root,
        string pointer,
        string keywordName)
    {
        List<SchemaNode> parsedBranches = [];
        foreach (var branch in branches)
        {
            var branchPointer = BuildUnionBranchPointer(branch.Element, pointer, keywordName, branch.Ordinal);
            var parsedBranch = Parse(branch.Element, root, branchPointer);
            if (parsedBranch is null)
            {
                return null;
            }

            parsedBranches.Add(parsedBranch);
        }

        return parsedBranches;
    }

    private SchemaNode? PromoteUnion(IReadOnlyList<SchemaNode> branches,
        UnionKeyword keyword,
        string root,
        string pointer,
        string location,
        string? description)
    {
        UnionNode node = new()
        {
            Branches = branches,
            Keyword = keyword,
            Description = description,
        };
        return Promote(node, root, pointer, location);
    }

    private static SchemaNode WrapNullable(SchemaNode inner, bool isNullable, string? description) => isNullable
        ? new NullableNode
        {
            Inner = inner,
            Description = description,
        }
        : inner;

    private bool RefuseUnionSiblings(JsonElement schema, string keywordName, string location)
    {
        var refused = false;
        foreach (var propertyName in schema
                     .EnumerateObject()
                     .Select(static property => property.Name))
        {
            if (propertyName is "description" || string.Equals(propertyName, keywordName, StringComparison.Ordinal))
            {
                continue;
            }

            _errors.Add(location, $"keyword '{propertyName}' cannot be combined with {keywordName}");
            refused = true;
        }

        return refused;
    }

    private static List<(JsonElement Element, int Ordinal)> DeduplicateRawRefBranches(JsonElement branchesElement)
    {
        HashSet<string> seenTargets = new(StringComparer.Ordinal);
        List<(JsonElement Element, int Ordinal)> branches = [];
        var ordinal = 0;
        foreach (var branch in branchesElement.EnumerateArray())
        {
            if (!TryReadRawRefTarget(branch, out var target) || seenTargets.Add(target))
            {
                branches.Add((branch, ordinal));
            }

            ordinal++;
        }

        return branches;
    }

    private static bool TryReadRawRefTarget(JsonElement branch, out string target)
    {
        target = string.Empty;
        if (branch.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var properties = branch.EnumerateObject();
        if (!properties.MoveNext())
        {
            return false;
        }

        var reference = properties.Current;
        if (properties.MoveNext()
            || !string.Equals(reference.Name, "$ref", StringComparison.Ordinal)
            || reference.Value.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        target = reference.Value.GetString() ?? string.Empty;
        return true;
    }

    private static List<(JsonElement Element, int Ordinal)> ExtractNullBranches(IEnumerable<(JsonElement Element, int Ordinal)> branches,
        out bool isNullable)
    {
        List<(JsonElement Element, int Ordinal)> nonNullBranches = [];
        isNullable = false;
        foreach (var branch in branches)
        {
            if (IsExactNullBranch(branch.Element))
            {
                isNullable = true;
                continue;
            }

            nonNullBranches.Add(branch);
        }

        return nonNullBranches;
    }

    private static bool IsExactNullBranch(JsonElement branch)
    {
        if (branch.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var sawType = false;
        var sawDescription = false;
        foreach (var property in branch.EnumerateObject())
        {
            if (string.Equals(property.Name, "type", StringComparison.Ordinal))
            {
                if (sawType
                    || property.Value.ValueKind is not JsonValueKind.String
                    || !string.Equals(property.Value.GetString(), "null", StringComparison.Ordinal))
                {
                    return false;
                }

                sawType = true;
                continue;
            }

            if (!string.Equals(property.Name, "description", StringComparison.Ordinal)
                || sawDescription
                || property.Value.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            sawDescription = true;
        }

        return sawType;
    }

    private static string BuildUnionBranchPointer(JsonElement branch, string pointer, string keywordName, int ordinal)
    {
        var branchKey = TryFindLiteralMarker(branch, out var markerName, out var markerValue)
            ? $"{markerName}={markerValue}"
            : ordinal.ToString(CultureInfo.InvariantCulture);
        return $"{pointer}/{keywordName}/{branchKey}";
    }

    private static bool TryFindLiteralMarker(JsonElement branch, out string markerName, out string markerValue)
    {
        markerName = string.Empty;
        markerValue = string.Empty;
        if (!IsObjectSchema(branch)
            || !branch.TryGetProperty("properties", out var properties)
            || properties.ValueKind is not JsonValueKind.Object
            || !TryReadRawRequiredNames(branch, out var requiredNames))
        {
            return false;
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (!requiredNames.Contains(property.Name)
                || !TryReadRawLiteralSchema(property.Value, out var value)
                || (markerName.Length != 0 && string.CompareOrdinal(property.Name, markerName) >= 0))
            {
                continue;
            }

            markerName = property.Name;
            markerValue = value;
        }

        return markerName.Length != 0;
    }

    private static bool IsObjectSchema(JsonElement schema) =>
        schema.ValueKind is JsonValueKind.Object
        && schema.TryGetProperty("type", out var type)
        && type.ValueKind is JsonValueKind.String
        && string.Equals(type.GetString(), "object", StringComparison.Ordinal);

    private static bool TryReadRawRequiredNames(JsonElement schema, out HashSet<string> requiredNames)
    {
        requiredNames = new HashSet<string>(StringComparer.Ordinal);
        if (!schema.TryGetProperty("required", out var required) || required.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        foreach (var requiredName in required.EnumerateArray())
        {
            if (requiredName.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            requiredNames.Add(requiredName.GetString() ?? string.Empty);
        }

        return true;
    }

    private static bool TryReadRawLiteralSchema(JsonElement schema, out string value)
    {
        value = string.Empty;
        if (schema.ValueKind is not JsonValueKind.Object
            || (schema.TryGetProperty("enum", out _) && schema.TryGetProperty("const", out _)))
        {
            return false;
        }

        if (schema.TryGetProperty("const", out var constValue))
        {
            return TryReadLiteralValue(constValue, out var constKind, out value)
                   && RawLiteralTypeMatches(schema, constKind, false);
        }

        if (!schema.TryGetProperty("enum", out var values) || values.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        var enumerator = values.EnumerateArray();
        if (!enumerator.MoveNext())
        {
            return false;
        }

        var literal = enumerator.Current;
        return !enumerator.MoveNext()
               && TryReadLiteralValue(literal, out var enumKind, out value)
               && RawLiteralTypeMatches(schema, enumKind, true);
    }

    private static bool RawLiteralTypeMatches(JsonElement schema, LiteralKind kind, bool requireType)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return !requireType;
        }

        return type.ValueKind is JsonValueKind.String
               && string.Equals(type.GetString(), GetLiteralTypeName(kind), StringComparison.Ordinal);
    }

    private SchemaNode? ParseArray(JsonElement schema, string root, string pointer, string location)
    {
        if (RefuseObjectOnlyKeywords(schema, location))
        {
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

        var hasItems = schema.TryGetProperty("items", out var items);
        var hasPrefixItems = schema.TryGetProperty("prefixItems", out var prefixItems);
        if (hasItems && hasPrefixItems)
        {
            _errors.Add(location, "items and prefixItems cannot be combined");
            return null;
        }

        if (hasPrefixItems)
        {
            return ParseTuple(schema, prefixItems, root, pointer, location, description);
        }

        if (!hasItems)
        {
            _errors.Add(location, "array without items");
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

    private TupleNode? ParseTuple(JsonElement schema,
        JsonElement prefixItems,
        string root,
        string pointer,
        string location,
        string? description)
    {
        if (prefixItems.ValueKind is not JsonValueKind.Array)
        {
            _errors.Add(location, "prefixItems must be an array");
            return null;
        }

        var itemCount = prefixItems.GetArrayLength();
        if (!ValidateTupleArity(schema, itemCount, location))
        {
            return null;
        }

        List<SchemaNode> items = [];
        var index = 0;
        foreach (var prefixItem in prefixItems.EnumerateArray())
        {
            var item = Parse(prefixItem, root, $"{pointer}/prefixItems/{index.ToString(CultureInfo.InvariantCulture)}");
            if (item is null)
            {
                return null;
            }

            items.Add(item);
            index++;
        }

        return new TupleNode
        {
            Items = items,
            Description = description,
        };
    }

    private bool ValidateTupleArity(JsonElement schema, int itemCount, string location)
    {
        var hasValidMinItems = ValidateTupleArity(schema, "minItems", itemCount, location);
        var hasValidMaxItems = ValidateTupleArity(schema, "maxItems", itemCount, location);
        return hasValidMinItems && hasValidMaxItems;
    }

    private bool ValidateTupleArity(JsonElement schema, string keyword, int itemCount, string location)
    {
        if (!schema.TryGetProperty(keyword, out var value))
        {
            return true;
        }

        if (value.ValueKind is JsonValueKind.Number
            && value.TryGetInt32(out var declaredItemCount)
            && declaredItemCount == itemCount)
        {
            return true;
        }

        _errors.Add(location, $"'{keyword}' must equal the prefixItems count");
        return false;
    }

    private SchemaNode? ParseObject(JsonElement schema, string root, string pointer, string location)
    {
        if (schema.TryGetProperty("enum", out _))
        {
            _errors.Add(location, "keyword 'enum' is only valid for string schemas");
            return null;
        }

        if (schema.TryGetProperty("items", out _))
        {
            _errors.Add(location, "keyword 'items' is only valid for array schemas");
            return null;
        }

        if (!TryReadOptionalString(schema, "description", location, out var description)
            || !TryReadRequiredNames(schema, location, out var requiredNames))
        {
            return null;
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties);
        var hasAdditionalProperties = schema.TryGetProperty("additionalProperties", out var additionalProperties);
        var hasPatternProperties = schema.TryGetProperty("patternProperties", out var patternProperties);
        if (hasPatternProperties && (hasProperties || hasAdditionalProperties))
        {
            _errors.Add(location, "patternProperties cannot be combined with properties or additionalProperties");
            return null;
        }

        if (hasProperties)
        {
            return ParsePropertyBag(schema, properties, requiredNames, root, pointer, location, description);
        }

        if (RefuseRequiredNamesWithoutProperties(requiredNames, location))
        {
            return null;
        }

        if (hasAdditionalProperties)
        {
            return ParseAdditionalPropertiesDictionary(additionalProperties, root, pointer, location, description);
        }

        return hasPatternProperties
            ? ParsePatternPropertiesDictionary(patternProperties, root, pointer, location, description)
            : new FreeFormObjectNode
            {
                Description = description
            };
    }

    private SchemaNode? ParsePropertyBag(JsonElement schema,
        JsonElement properties,
        IReadOnlyList<string> requiredNames,
        string root,
        string pointer,
        string location,
        string? description)
    {
        if (properties.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(location, "properties must be an object");
            return null;
        }

        if (!ValidateRequiredPropertyNames(requiredNames, properties, location)
            || !TryReadAdditionalProperties(schema, location, out var additionalProperties, out var additionalPropertiesSchema))
        {
            return null;
        }

        var parsedProperties = ParseProperties(properties, requiredNames, root, pointer);
        if (parsedProperties is null)
        {
            return null;
        }

        SchemaNode? parsedAdditionalPropertiesSchema = null;
        if (additionalProperties is AdditionalPropertiesKind.Schema)
        {
            parsedAdditionalPropertiesSchema = Parse(additionalPropertiesSchema, root, $"{pointer}/additionalProperties");
            if (parsedAdditionalPropertiesSchema is null)
            {
                return null;
            }
        }

        var literalMarkers = CollectLiteralMarkers(parsedProperties);
        ObjectNode node = new()
        {
            Properties = parsedProperties,
            LiteralMarkers = literalMarkers,
            ErrorStyle = ClassifyErrorStyle(parsedProperties, literalMarkers),
            AdditionalProperties = additionalProperties,
            AdditionalPropertiesSchema = parsedAdditionalPropertiesSchema,
            Description = description,
        };
        return Promote(node, root, pointer, location);
    }

    private List<SpecProperty>? ParseProperties(JsonElement properties,
        IReadOnlyList<string> requiredNames,
        string root,
        string pointer)
    {
        List<SpecProperty> parsedProperties = [];
        foreach (var property in properties.EnumerateObject())
        {
            var propertySchema = Parse(property.Value, root, $"{pointer}/properties/{property.Name}");
            if (propertySchema is null)
            {
                return null;
            }

            parsedProperties.Add(new SpecProperty
            {
                Name = property.Name,
                Schema = propertySchema,
                IsRequired = requiredNames.Contains(property.Name, StringComparer.Ordinal),
            });
        }

        return parsedProperties;
    }

    private static IReadOnlyList<LiteralMarker> CollectLiteralMarkers(IEnumerable<SpecProperty> properties) =>
    [
        .. properties
            .Where(static property => property is { IsRequired: true, Schema: LiteralNode })
            .Select(static property =>
            {
                var literal = (LiteralNode)property.Schema;
                return new LiteralMarker
                {
                    PropertyName = property.Name,
                    Kind = literal.Kind,
                    Value = literal.Value,
                };
            })
    ];

    private static ErrorStyle ClassifyErrorStyle(IReadOnlyList<SpecProperty> properties,
        IReadOnlyList<LiteralMarker> literalMarkers)
    {
        if (literalMarkers.Any(static marker => string.Equals(marker.PropertyName, "_tag", StringComparison.Ordinal)))
        {
            return ErrorStyle.EffectTag;
        }

        var hasNameMarker = literalMarkers.Any(static marker => string.Equals(marker.PropertyName, "name", StringComparison.Ordinal));
        var hasRequiredData = properties.Any(static property =>
            property.IsRequired && string.Equals(property.Name, "data", StringComparison.Ordinal));
        return hasNameMarker && hasRequiredData ? ErrorStyle.NameData : ErrorStyle.None;
    }

    private DictionaryNode? ParseAdditionalPropertiesDictionary(JsonElement additionalProperties,
        string root,
        string pointer,
        string location,
        string? description)
    {
        if (additionalProperties.ValueKind is JsonValueKind.True)
        {
            _errors.Add(location, "additionalProperties: true is outside the dialect");
            return null;
        }

        if (additionalProperties.ValueKind is JsonValueKind.False)
        {
            _errors.Add(location, "additionalProperties: false requires a properties object");
            return null;
        }

        if (additionalProperties.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(location, "additionalProperties must be false or a schema");
            return null;
        }

        var value = Parse(additionalProperties, root, $"{pointer}/additionalProperties");
        return value is null
            ? null
            : new DictionaryNode
            {
                Value = value,
                Description = description
            };
    }

    private DictionaryNode? ParsePatternPropertiesDictionary(JsonElement patternProperties,
        string root,
        string pointer,
        string location,
        string? description)
    {
        if (patternProperties.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(location, "patternProperties must be an object");
            return null;
        }

        var patterns = patternProperties.EnumerateObject();
        if (!patterns.MoveNext())
        {
            _errors.Add(location, "patternProperties must contain exactly one pattern");
            return null;
        }

        var pattern = patterns.Current;
        if (patterns.MoveNext())
        {
            _errors.Add(location, "patternProperties must contain exactly one pattern");
            return null;
        }

        var value = Parse(pattern.Value, root, $"{pointer}/patternProperties");
        return value is null
            ? null
            : new DictionaryNode
            {
                Value = value,
                Description = description
            };
    }

    private bool TryReadRequiredNames(JsonElement schema, string location, out IReadOnlyList<string> requiredNames)
    {
        List<string> names = [];
        requiredNames = names;
        if (!schema.TryGetProperty("required", out var required))
        {
            return true;
        }

        if (required.ValueKind is not JsonValueKind.Array)
        {
            _errors.Add(location, "required must be an array");
            return false;
        }

        foreach (var requiredName in required.EnumerateArray())
        {
            if (requiredName.ValueKind is not JsonValueKind.String)
            {
                _errors.Add(location, "required names must be strings");
                return false;
            }

            names.Add(requiredName.GetString() ?? string.Empty);
        }

        return true;
    }

    private bool ValidateRequiredPropertyNames(IReadOnlyList<string> requiredNames, JsonElement properties, string location)
    {
        var valid = true;
        foreach (var requiredName in requiredNames)
        {
            if (properties.TryGetProperty(requiredName, out _))
            {
                continue;
            }

            _errors.Add(location, $"required property '{requiredName}' is missing from properties");
            valid = false;
        }

        return valid;
    }

    private bool RefuseRequiredNamesWithoutProperties(IReadOnlyList<string> requiredNames, string location)
    {
        if (requiredNames.Count == 0)
        {
            return false;
        }

        foreach (var requiredName in requiredNames)
        {
            _errors.Add(location, $"required property '{requiredName}' is missing from properties");
        }

        return true;
    }

    private bool TryReadAdditionalProperties(JsonElement schema,
        string location,
        out AdditionalPropertiesKind additionalProperties,
        out JsonElement additionalPropertiesSchema)
    {
        additionalPropertiesSchema = default;
        if (!schema.TryGetProperty("additionalProperties", out var value))
        {
            additionalProperties = AdditionalPropertiesKind.Open;
            return true;
        }

        if (value.ValueKind is JsonValueKind.False)
        {
            additionalProperties = AdditionalPropertiesKind.Forbidden;
            return true;
        }

        if (value.ValueKind is JsonValueKind.True)
        {
            _errors.Add(location, "additionalProperties: true is outside the dialect");
            additionalProperties = default;
            return false;
        }

        if (value.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(location, "additionalProperties must be false or a schema");
            additionalProperties = default;
            return false;
        }

        additionalProperties = AdditionalPropertiesKind.Schema;
        additionalPropertiesSchema = value;
        return true;
    }

    private bool RefuseObjectOnlyKeywords(JsonElement schema, string location)
    {
        var refused = false;
        foreach (var propertyName in schema
                     .EnumerateObject()
                     .Select(static property => property.Name)
                     .Where(static propertyName => propertyName is "properties" or "required" or "additionalProperties" or "patternProperties"))
        {
            _errors.Add(location, $"keyword '{propertyName}' is only valid for object schemas");
            refused = true;
        }

        return refused;
    }

    private bool RefuseItemsKeyword(JsonElement schema, string location)
    {
        if (!schema.TryGetProperty("items", out _))
        {
            return false;
        }

        _errors.Add(location, "keyword 'items' is only valid for array schemas");
        return true;
    }

    private bool ValidateLiteralType(JsonElement schema, LiteralKind kind, string location)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return true;
        }

        if (type.ValueKind is JsonValueKind.Array)
        {
            _errors.Add(location, "type array is outside the dialect");
            return false;
        }

        if (type.ValueKind is not JsonValueKind.String)
        {
            _errors.Add(location, "schema type must be a string");
            return false;
        }

        var expectedType = GetLiteralTypeName(kind);
        if (string.Equals(type.GetString(), expectedType, StringComparison.Ordinal))
        {
            return true;
        }

        _errors.Add(location, $"const value does not match schema type '{type.GetString()}'");
        return false;
    }

    private static bool TryReadLiteralValue(JsonElement valueElement, out LiteralKind kind, out string value)
    {
        switch (valueElement.ValueKind)
        {
            case JsonValueKind.String:
                kind = LiteralKind.String;
                value = valueElement.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.True:
                kind = LiteralKind.Boolean;
                value = "true";
                return true;
            case JsonValueKind.False:
                kind = LiteralKind.Boolean;
                value = "false";
                return true;
            default:
                kind = default;
                value = string.Empty;
                return false;
        }
    }

    private static string GetLiteralTypeName(LiteralKind kind) => kind is LiteralKind.String ? "string" : "boolean";

    private SchemaNode? Promote(SchemaNode node, string root, string pointer, string location)
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

    private SchemaNode? RefuseNullType(string location)
    {
        _errors.Add(location, "null schemas are only valid as exact union branches");
        return null;
    }
}
