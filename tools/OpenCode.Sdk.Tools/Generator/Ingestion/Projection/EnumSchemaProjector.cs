using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class EnumSchemaProjector
{
    private readonly SchemaShapeClassifier _shapeClassifier;

    public EnumSchemaProjector(SchemaShapeClassifier shapeClassifier)
    {
        _shapeClassifier = shapeClassifier ?? throw new ArgumentNullException(nameof(shapeClassifier));
    }

    public EnumNode? Project(OpenApiSchema schema, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (_shapeClassifier.Classify(schema, location, errors) is not CoreSchemaShape.Enum || schema.Enum is not { Count: > 1 } values)
        {
            if (schema.Enum is not { Count: > 1 })
            {
                errors.Add(location, "only string enums with multiple values are supported by core schema projection");
            }

            return null;
        }

        var projectedValues = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is not JsonValue value || !value.TryGetValue<string>(out var projectedValue))
            {
                errors.Add(location, "enum values must all be strings");
                return null;
            }

            projectedValues[index] = projectedValue;
        }

        return new EnumNode
        {
            Description = schema.Description,
            Format = schema.Format,
            Values = projectedValues,
        };
    }
}
