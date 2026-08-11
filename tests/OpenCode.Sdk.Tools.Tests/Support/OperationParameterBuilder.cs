using System.Text.Json.Nodes;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class OperationParameterBuilder(JsonObject operation)
{
    private readonly JsonObject _operation = operation ?? throw new ArgumentNullException(nameof(operation));

    public void Add(string name, string location, Action<SchemaBuilder> configure, bool required, bool deepObject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(configure);

        var parameter = new JsonObject
        {
            ["name"] = name,
            ["in"] = location,
            ["schema"] = BuildSchema(configure),
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

        GetParameters().Add(parameter);
    }

    public void AddWithStyle(string name, string style, bool explode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(style);

        Add(name, "query", schema => schema.Type("string"), required: false, deepObject: false);
        var parameter = GetParameters()[^1]!.AsObject();
        parameter["style"] = style;
        parameter["explode"] = explode;
    }

    public void AddContent(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        GetParameters()
            .Add(new JsonObject
            {
                ["name"] = name,
                ["in"] = "query",
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject
                    {
                        ["schema"] = new JsonObject
                        {
                            ["type"] = "string",
                        },
                    },
                },
            });
    }

    private JsonArray GetParameters()
    {
        if (_operation["parameters"] is JsonArray parameters)
        {
            return parameters;
        }

        parameters = [];
        _operation["parameters"] = parameters;
        return parameters;
    }

    private static JsonObject BuildSchema(Action<SchemaBuilder> configure)
    {
        var schema = new SchemaBuilder();
        configure(schema);
        return schema.Build();
    }
}
