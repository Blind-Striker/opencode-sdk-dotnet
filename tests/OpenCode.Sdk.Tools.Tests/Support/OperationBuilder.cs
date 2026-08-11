using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class OperationBuilder
{
    private readonly JsonObject _operation;
    private readonly JsonObject _responses = [];

    public OperationBuilder(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        _operation = new JsonObject
        {
            ["operationId"] = operationId,
            ["responses"] = _responses,
        };
    }

    public OperationBuilder Parameter(string name, string location, Action<SchemaBuilder> configure, bool required = false, bool deepObject = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(configure);

        if (_operation["parameters"] is not JsonArray parameters)
        {
            parameters = [];
            _operation["parameters"] = parameters;
        }

        var schema = new SchemaBuilder();
        configure(schema);
        var parameter = new JsonObject
        {
            ["name"] = name,
            ["in"] = location,
            ["schema"] = schema.Build(),
        };

        if (required)
        {
            parameter["required"] = true;
        }

        if (deepObject)
        {
            parameter["style"] = "deepObject";
            parameter["explode"] = true;
        }

        parameters.Add(parameter);
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

    public OperationBuilder Response(int status, string? mediaType = null, Action<SchemaBuilder>? schema = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(status, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599);

        if (mediaType is null && schema is not null)
        {
            throw new ArgumentException("A response schema requires a media type.", nameof(mediaType));
        }

        var response = new JsonObject
        {
            ["description"] = "Response",
        };

        if (mediaType is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
            var media = new JsonObject();
            if (schema is not null)
            {
                var schemaBuilder = new SchemaBuilder();
                schema(schemaBuilder);
                media["schema"] = schemaBuilder.Build();
            }

            response["content"] = new JsonObject
            {
                [mediaType] = media,
            };
        }

        _responses[status.ToString(CultureInfo.InvariantCulture)] = response;
        return this;
    }

    public OperationBuilder SseResponse(Action<SchemaBuilder> schema, string? effectStreamJson = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var media = new JsonObject
        {
            ["schema"] = BuildSchema(schema),
        };
        if (effectStreamJson is not null)
        {
            media["x-effect-stream"] = JsonNode.Parse(effectStreamJson);
        }

        _responses["200"] = new JsonObject
        {
            ["description"] = "Event stream",
            ["content"] = new JsonObject
            {
                ["text/event-stream"] = media,
            },
        };
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

    internal JsonObject Build() => _operation.DeepClone().AsObject();

    private static JsonObject BuildSchema(Action<SchemaBuilder> configure)
    {
        var schema = new SchemaBuilder();
        configure(schema);
        return schema.Build();
    }
}
