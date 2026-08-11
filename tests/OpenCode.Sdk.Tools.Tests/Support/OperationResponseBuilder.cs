using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class OperationResponseBuilder(JsonObject responses, FixtureLoader fixtureLoader)
{
    private readonly FixtureLoader _fixtureLoader = fixtureLoader ?? throw new ArgumentNullException(nameof(fixtureLoader));
    private readonly JsonObject _responses = responses ?? throw new ArgumentNullException(nameof(responses));

    public void Add(int status, string? mediaType, Action<SchemaBuilder>? configureSchema)
    {
        ValidateStatus(status);
        if (mediaType is null && configureSchema is not null)
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
            if (configureSchema is not null)
            {
                media["schema"] = BuildSchema(configureSchema);
            }

            response["content"] = new JsonObject
            {
                [mediaType] = media,
            };
        }

        _responses[FormatStatus(status)] = response;
    }

    public void AddFromFixture(int status, string mediaType, string fixtureName)
    {
        ValidateStatus(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureName);

        _responses[FormatStatus(status)] = CreateResponse(mediaType, JsonNode.Parse(_fixtureLoader.Load(fixtureName)));
    }

    public void AddWithHeader(int status)
    {
        Add(status, mediaType: null, configureSchema: null);
        _responses[FormatStatus(status)]!["headers"] = new JsonObject
        {
            ["x-test"] = new JsonObject
            {
                ["schema"] = new JsonObject
                {
                    ["type"] = "string",
                },
            },
        };
    }

    public void AddWithItemSchema(int status)
    {
        Add(status, "application/json", configureSchema: null);
        var media = _responses[FormatStatus(status)]!["content"]!["application/json"]!;
        media["itemSchema"] = new JsonObject
        {
            ["type"] = "string",
        };
    }

    public void AddMediaTypes(int status, IReadOnlyList<string> mediaTypes)
    {
        ValidateStatus(status);
        ArgumentNullException.ThrowIfNull(mediaTypes);

        var content = new JsonObject();
        foreach (var mediaType in mediaTypes)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                throw new ArgumentException("Media types cannot contain null or whitespace.", nameof(mediaTypes));
            }

            content[mediaType] = new JsonObject();
        }

        _responses[FormatStatus(status)] = new JsonObject
        {
            ["description"] = "Response",
            ["content"] = content,
        };
    }

    public void AddWithEncoding(int status)
    {
        Add(status, "application/json", schema => schema
            .Type("object")
            .Property("data", property => property.Type("string")));
        var media = _responses[FormatStatus(status)]!["content"]!["application/json"]!;
        media["encoding"] = new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["contentType"] = "text/plain",
            },
        };
    }

    public void AddDefault()
    {
        _responses["default"] = new JsonObject
        {
            ["description"] = "Default response",
        };
    }

    public void AddSse(Action<SchemaBuilder> configureSchema, string? effectStreamJson)
    {
        ArgumentNullException.ThrowIfNull(configureSchema);

        var media = new JsonObject
        {
            ["schema"] = BuildSchema(configureSchema),
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
    }

    public void AddSseFromFixture(Action<SchemaBuilder> configureSchema, string effectStreamFixtureName)
    {
        ArgumentNullException.ThrowIfNull(configureSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectStreamFixtureName);
        AddSse(configureSchema, _fixtureLoader.Load(effectStreamFixtureName));
    }

    public void AddJsonWithEffectStreamFromFixture(string effectStreamFixtureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectStreamFixtureName);

        Add(200, "application/json", schema => schema.Type("string"));
        var media = _responses["200"]!["content"]!["application/json"]!;
        media["x-effect-stream"] = JsonNode.Parse(_fixtureLoader.Load(effectStreamFixtureName));
    }

    private static JsonObject BuildSchema(Action<SchemaBuilder> configure)
    {
        var schema = new SchemaBuilder();
        configure(schema);
        return schema.Build();
    }

    private static JsonObject CreateResponse(string mediaType, JsonNode? schema) =>
        new()
        {
            ["description"] = "Response",
            ["content"] = new JsonObject
            {
                [mediaType] = new JsonObject
                {
                    ["schema"] = schema,
                },
            },
        };

    private static string FormatStatus(int status) => status.ToString(CultureInfo.InvariantCulture);

    private static void ValidateStatus(int status)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(status, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599);
    }
}
