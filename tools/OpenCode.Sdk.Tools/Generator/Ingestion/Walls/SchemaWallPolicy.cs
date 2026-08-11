using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class SchemaWallPolicy
{
    private const string PrefixItemsAdmissionLocation = "Config/properties/plugin/items/anyOf/1";

    private readonly Func<OpenApiSchema, bool>[] _admittedConstraintRules =
    [
        static schema => schema.Type is not null,
        static schema => schema.Const is not null,
        static schema => schema.Enum is { Count: > 0 },
        static schema => schema.OneOf is { Count: > 0 },
        static schema => schema.AnyOf is { Count: > 0 },
        static schema => schema.AllOf is { Count: > 0 },
        static schema => schema.Required is { Count: > 0 },
        static schema => schema.Items is not null,
        static schema => schema.Properties is { Count: > 0 },
        static schema => schema.PatternProperties is { Count: > 0 },
        static schema => !schema.AdditionalPropertiesAllowed,
        static schema => schema.AdditionalProperties is not null,
        static schema => schema.ContentEncoding is not null,
        static schema => schema.ContentMediaType is not null,
        static schema => schema.ContentSchema is not null,
    ];

    private readonly SchemaRule[] _rejectedRules =
    [
        new("allOf", static schema => schema.AllOf is { Count: > 0 }),
        new("type arrays", static schema => schema.Type is { } type && ((int)type & ((int)type - 1)) != 0),
        new("discriminator", static schema => schema.Discriminator is not null),
        new("not", static schema => schema.Not is not null),
        new("if", static schema => schema.If is not null),
        new("then", static schema => schema.Then is not null),
        new("else", static schema => schema.Else is not null),
        new("dependentSchemas", static schema => schema.DependentSchemas is { Count: > 0 }),
        new("dependentRequired", static schema => schema.DependentRequired is { Count: > 0 }),
        new("propertyNames", static schema => schema.PropertyNames is not null),
        new("contains", static schema => schema.Contains is not null),
        new("unevaluatedProperties", static schema => !schema.UnevaluatedProperties),
        new("unevaluatedProperties", static schema => schema.UnevaluatedPropertiesSchema is not null),
        new("$defs", static schema => schema.Definitions is { Count: > 0 }),
        new("$dynamicRef", static schema => schema.DynamicRef is not null),
        new("$dynamicAnchor", static schema => schema.DynamicAnchor is not null),
        new("$schema", static schema => schema.Schema is not null),
        new("$id", static schema => schema.Id is not null),
        new("$anchor", static schema => schema.Anchor is not null),
        new("$comment", static schema => schema.Comment is not null),
        new("$vocabulary", static schema => schema.Vocabulary is { Count: > 0 }),
        new("title", static schema => schema.Title is not null),
        new("default", static schema => schema.Default is not null),
        new("examples", static schema => schema.Examples is { Count: > 0 }),
        new("example", static schema => schema.Example is not null),
        new("readOnly", static schema => schema.ReadOnly),
        new("writeOnly", static schema => schema.WriteOnly),
        new("xml", static schema => schema.Xml is not null),
        new("externalDocs", static schema => schema.ExternalDocs is not null),
        new("minLength", static schema => schema.MinLength is not null),
        new("maxLength", static schema => schema.MaxLength is not null),
        new("multipleOf", static schema => schema.MultipleOf is not null),
        new("exclusiveMaximum", static schema => schema.ExclusiveMaximum is not null),
        new("minProperties", static schema => schema.MinProperties is not null),
        new("maxProperties", static schema => schema.MaxProperties is not null),
        new("uniqueItems", static schema => schema.UniqueItems is not null),
        new("minContains", static schema => schema.MinContains is not null),
        new("maxContains", static schema => schema.MaxContains is not null),
        new("deprecated", static schema => schema.Deprecated),
    ];

    public void Check(OpenApiSchema schema, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        foreach (var rule in _rejectedRules.Where(rule => rule.IsPopulated(schema)))
        {
            errors.Add(location, $"schema keyword '{rule.Keyword}' is not supported");
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

    public bool IsUnrestricted(OpenApiSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return !_admittedConstraintRules.Any(rule => rule(schema));
    }

    private static bool IsAdmittedPrefixItems(string keyword, string location) =>
        string.Equals(keyword, "prefixItems", StringComparison.Ordinal)
        && string.Equals(location, PrefixItemsAdmissionLocation, StringComparison.Ordinal);

    private sealed record SchemaRule(string Keyword, Func<OpenApiSchema, bool> IsPopulated);
}
