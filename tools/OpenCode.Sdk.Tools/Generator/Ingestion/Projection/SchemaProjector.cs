using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class SchemaProjector
{
    private readonly GraphKeyBuilder _keys;
    private readonly SchemaWallPolicy _wall;

    public SchemaProjector(SchemaWallPolicy wall, GraphKeyBuilder keys)
    {
        _wall = wall ?? throw new ArgumentNullException(nameof(wall));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public SchemaNode? Project(IOpenApiSchema schema, string root, string pointer, ProjectionState state)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentNullException.ThrowIfNull(state);

        var location = string.Concat(root, pointer);
        if (schema is OpenApiSchemaReference reference)
        {
            return state.Register(ProjectReference(reference, location, state), root, pointer, location);
        }

        if (schema is not OpenApiSchema concrete)
        {
            state.Errors.Add(location, $"schema implementation '{schema.GetType().Name}' is not supported");
            return null;
        }

        if (!state.TryVisit(concrete))
        {
            state.Errors.Add(location, "schema identity was visited more than once");
            return null;
        }

        _wall.Check(concrete, location, state.Errors);
        var projected = ProjectConcrete(concrete, root, pointer, location, state);
        return state.Register(projected, root, pointer, location);
    }

    private SchemaNode? ProjectConcrete(OpenApiSchema schema, string root, string pointer, string location, ProjectionState state)
    {
        if (schema.Enum is { Count: > 0 })
        {
            return ProjectEnum(schema, location, state);
        }

        return schema.Type switch
        {
            JsonSchemaType.String => CreatePrimitive(schema, PrimitiveKind.String),
            JsonSchemaType.Number => CreatePrimitive(schema, PrimitiveKind.Number),
            JsonSchemaType.Integer => CreatePrimitive(schema, PrimitiveKind.Integer),
            JsonSchemaType.Boolean => CreatePrimitive(schema, PrimitiveKind.Boolean),
            JsonSchemaType.Array => ProjectArray(schema, root, pointer, location, state),
            JsonSchemaType.Null or JsonSchemaType.Object => RefuseUnsupportedShape(schema, location, state),
            null when _wall.IsUnrestricted(schema) => CreateUnrestricted(schema),
            _ => RefuseUnsupportedShape(schema, location, state),
        };
    }

    private static EnumNode? ProjectEnum(OpenApiSchema schema, string location, ProjectionState state)
    {
        if (schema.Type is not JsonSchemaType.String || schema.Enum is not { Count: > 1 } values)
        {
            state.Errors.Add(location, "only string enums with multiple values are supported by core schema projection");
            return null;
        }

        var projectedValues = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is not JsonValue value || !value.TryGetValue<string>(out var projectedValue))
            {
                state.Errors.Add(location, "enum values must all be strings");
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

    private ArrayNode? ProjectArray(OpenApiSchema schema, string root, string pointer, string location, ProjectionState state)
    {
        if (schema.Items is null)
        {
            state.Errors.Add(location, "array schema must declare items");
            return null;
        }

        var item = Project(schema.Items, root, _keys.Append(pointer, "items"), state);
        return item is null
            ? null
            : new ArrayNode
            {
                Description = schema.Description,
                Format = schema.Format,
                Item = item,
            };
    }

    private static PrimitiveNode CreatePrimitive(OpenApiSchema schema, PrimitiveKind kind) =>
        new()
        {
            Description = schema.Description,
            Format = schema.Format,
            Kind = kind,
        };

    private static UnrestrictedNode CreateUnrestricted(OpenApiSchema schema) =>
        new()
        {
            Description = schema.Description,
            Format = schema.Format,
        };

    private static RefNode? ProjectReference(OpenApiSchemaReference reference, string location, ProjectionState state)
    {
        var target = reference.Reference.Id;
        if (!string.IsNullOrWhiteSpace(target) && reference.Target is not null)
        {
            return new RefNode
            {
                Target = target,
            };
        }

        state.Errors.Add(location, $"reference target '{target ?? "<missing>"}' could not be resolved");
        return null;
    }

    private static SchemaNode? RefuseUnsupportedShape(OpenApiSchema schema, string location, ProjectionState state)
    {
        state.Errors.Add(location, $"schema type '{schema.Type?.ToString() ?? "unspecified"}' is not supported by core schema projection");
        return null;
    }
}
