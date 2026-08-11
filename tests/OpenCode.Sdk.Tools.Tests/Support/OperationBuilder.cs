using System.Text.Json.Nodes;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class OperationBuilder
{
    private readonly JsonObject _operation;
    private readonly OperationParameterBuilder _parameters;
    private readonly OperationResponseBuilder _responses;

    public OperationBuilder(string operationId, FixtureLoader fixtureLoader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(fixtureLoader);

        var responses = new JsonObject();
        _operation = new JsonObject
        {
            ["operationId"] = operationId,
            ["responses"] = responses,
        };
        _parameters = new OperationParameterBuilder(_operation);
        _responses = new OperationResponseBuilder(responses, fixtureLoader);
        Response(200);
    }

    public OperationBuilder Parameter(string name, string location, Action<SchemaBuilder> configure, bool required = false, bool deepObject = false)
    {
        _parameters.Add(name, location, configure, required, deepObject);
        return this;
    }

    public OperationBuilder ParameterWithStyle(string name, string style, bool explode)
    {
        _parameters.AddWithStyle(name, style, explode);
        return this;
    }

    public OperationBuilder ContentParameter(string name)
    {
        _parameters.AddContent(name);
        return this;
    }

    public OperationBuilder RequestBody(string mediaType, Action<SchemaBuilder> configure, bool required = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(configure);

        var requestBody = new JsonObject
        {
            ["content"] = new JsonObject
            {
                [mediaType] = new JsonObject
                {
                    ["schema"] = BuildSchema(configure),
                },
            },
        };
        if (required)
        {
            requestBody["required"] = true;
        }

        _operation["requestBody"] = requestBody;
        return this;
    }

    public OperationBuilder RequestBodyMediaTypes(params string[] mediaTypes)
    {
        ArgumentNullException.ThrowIfNull(mediaTypes);

        var content = new JsonObject();
        foreach (var mediaType in mediaTypes)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                throw new ArgumentException("Media types cannot contain null or whitespace.", nameof(mediaTypes));
            }

            content[mediaType] = new JsonObject
            {
                ["schema"] = new JsonObject
                {
                    ["type"] = "string",
                },
            };
        }

        _operation["requestBody"] = new JsonObject
        {
            ["content"] = content,
        };
        return this;
    }

    public OperationBuilder Response(int status, string? mediaType = null, Action<SchemaBuilder>? schema = null)
    {
        _responses.Add(status, mediaType, schema);
        return this;
    }

    public OperationBuilder ResponseFromFixture(int status, string mediaType, string fixtureName)
    {
        _responses.AddFromFixture(status, mediaType, fixtureName);
        return this;
    }

    public OperationBuilder ResponseMediaTypes(int status, params string[] mediaTypes)
    {
        _responses.AddMediaTypes(status, mediaTypes);
        return this;
    }

    public OperationBuilder DefaultResponse()
    {
        _responses.AddDefault();
        return this;
    }

    public OperationBuilder SseResponse(Action<SchemaBuilder> schema, string? effectStreamJson = null)
    {
        _responses.AddSse(schema, effectStreamJson);
        return this;
    }

    public OperationBuilder SseResponseFromFixture(Action<SchemaBuilder> schema, string effectStreamFixtureName)
    {
        _responses.AddSseFromFixture(schema, effectStreamFixtureName);
        return this;
    }

    public OperationBuilder JsonResponseWithEffectStreamFromFixture(string effectStreamFixtureName)
    {
        _responses.AddJsonWithEffectStreamFromFixture(effectStreamFixtureName);
        return this;
    }

    public OperationBuilder Extension(string key, string rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(rawJson);

        return !key.StartsWith("x-", StringComparison.Ordinal)
            ? throw new ArgumentException("OpenAPI extension keys must start with 'x-'.", nameof(key))
            : Raw(key, rawJson);
    }

    public OperationBuilder Raw(string key, string rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(rawJson);

        _operation[key] = JsonNode.Parse(rawJson);
        return this;
    }

    public OperationBuilder Deprecated()
    {
        _operation["deprecated"] = true;
        return this;
    }

    public OperationBuilder Summary(string summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        _operation["summary"] = summary;
        return this;
    }

    public OperationBuilder Description(string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        _operation["description"] = description;
        return this;
    }

    public OperationBuilder WithIgnoredTagsAndSecurity()
    {
        _operation["tags"] = new JsonArray("test");
        _operation["security"] = new JsonArray();
        return this;
    }

    internal JsonObject Build() => _operation.DeepClone().AsObject();

    private static JsonObject BuildSchema(Action<SchemaBuilder> configure)
    {
        var schema = new SchemaBuilder();
        configure(schema);
        return schema.Build();
    }
}
