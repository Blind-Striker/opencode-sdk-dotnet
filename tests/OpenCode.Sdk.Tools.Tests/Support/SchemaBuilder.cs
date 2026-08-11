using System.Text.Json.Nodes;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class SchemaBuilder
{
    private readonly JsonObject _schema = [];

    public SchemaBuilder Type(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        _schema["type"] = type;
        return this;
    }

    public SchemaBuilder Property(string name, Action<SchemaBuilder> configure, bool required = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (_schema["properties"] is not JsonObject properties)
        {
            properties = [];
            _schema["properties"] = properties;
        }

        var property = new SchemaBuilder();
        configure(property);
        properties[name] = property.Build();

        if (required)
        {
            Required(name);
        }

        return this;
    }

    public SchemaBuilder Required(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (_schema["required"] is not JsonArray required)
        {
            required = [];
            _schema["required"] = required;
        }

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Required names cannot contain null or whitespace.", nameof(names));
            }

            if (required.All(node => node?.GetValue<string>() != name))
            {
                required.Add(name);
            }
        }

        return this;
    }

    public SchemaBuilder Enum(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var enumValues = new JsonArray();
        foreach (var value in values)
        {
            if (value is null)
            {
                throw new ArgumentException("Enum values cannot contain null.", nameof(values));
            }

            enumValues.Add(value);
        }

        _schema["enum"] = enumValues;
        return this;
    }

    public SchemaBuilder Const(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _schema["const"] = value;
        return this;
    }

    public SchemaBuilder AnyOf(params Action<SchemaBuilder>[] configure) => AddComposition("anyOf", configure);

    public SchemaBuilder OneOf(params Action<SchemaBuilder>[] configure) => AddComposition("oneOf", configure);

    public SchemaBuilder AllOf(params Action<SchemaBuilder>[] configure) => AddComposition("allOf", configure);

    public SchemaBuilder AdditionalPropertiesFalse()
    {
        _schema["additionalProperties"] = false;
        return this;
    }

    public SchemaBuilder Ref(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        _schema["$ref"] = $"#/components/schemas/{target}";
        return this;
    }

    public SchemaBuilder Items(Action<SchemaBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _schema["items"] = BuildSchema(configure);
        return this;
    }

    public SchemaBuilder PrefixItems(params Action<SchemaBuilder>[] configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var items = new JsonArray();
        foreach (var item in configure)
        {
            if (item is null)
            {
                throw new ArgumentException("Prefix item configurations cannot contain null.", nameof(configure));
            }

            items.Add(BuildSchema(item));
        }

        _schema["prefixItems"] = items;
        return this;
    }

    public SchemaBuilder AdditionalProperties(Action<SchemaBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _schema["additionalProperties"] = BuildSchema(configure);
        return this;
    }

    public SchemaBuilder PatternProperties(string pattern, Action<SchemaBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(configure);

        if (_schema["patternProperties"] is not JsonObject patterns)
        {
            patterns = [];
            _schema["patternProperties"] = patterns;
        }

        patterns[pattern] = BuildSchema(configure);
        return this;
    }

    public SchemaBuilder ContentSchema(string mediaType, Action<SchemaBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(configure);

        _schema["contentMediaType"] = mediaType;
        _schema["contentSchema"] = BuildSchema(configure);
        return this;
    }

    public SchemaBuilder Unrestricted()
    {
        _schema.Clear();
        return this;
    }

    public SchemaBuilder Raw(string key, string rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(rawJson);

        _schema[key] = JsonNode.Parse(rawJson);
        return this;
    }

    public SchemaBuilder Description(string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        _schema["description"] = description;
        return this;
    }

    public SchemaBuilder Format(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        _schema["format"] = format;
        return this;
    }

    internal JsonObject Build() => _schema.DeepClone().AsObject();

    private SchemaBuilder AddComposition(string keyword, IEnumerable<Action<SchemaBuilder>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var branches = new JsonArray();
        foreach (var branch in configure)
        {
            if (branch is null)
            {
                throw new ArgumentException("Composition configurations cannot contain null.", nameof(configure));
            }

            branches.Add(BuildSchema(branch));
        }

        _schema[keyword] = branches;
        return this;
    }

    private static JsonObject BuildSchema(Action<SchemaBuilder> configure)
    {
        var schema = new SchemaBuilder();
        configure(schema);
        return schema.Build();
    }
}
