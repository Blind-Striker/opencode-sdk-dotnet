using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class ParameterProjector
{
    private readonly GraphKeyBuilder _keys;
    private readonly SchemaProjector _schemaProjector;
    private readonly ParameterWallPolicy _wall;

    public ParameterProjector(SchemaProjector schemaProjector, GraphKeyBuilder keys, ParameterWallPolicy wall)
    {
        _schemaProjector = schemaProjector ?? throw new ArgumentNullException(nameof(schemaProjector));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _wall = wall ?? throw new ArgumentNullException(nameof(wall));
    }

    public IReadOnlyList<SpecParameter> Project(OpenApiOperation operation, JsonObject rawOperation, string root, ProjectionState state)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(rawOperation);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(state);

        if (operation.Parameters is not { } parameters)
        {
            return Array.AsReadOnly(Array.Empty<SpecParameter>());
        }

        if (rawOperation["parameters"] is not JsonArray rawParameters || rawParameters.Count != parameters.Count)
        {
            state.Errors.Add(root, "raw operation parameters did not match the typed operation");
            return Array.AsReadOnly(Array.Empty<SpecParameter>());
        }

        var projected = new List<SpecParameter>(parameters.Count);
        var identities = new HashSet<(string Name, ParameterLocation Location)>();
        for (var index = 0; index < parameters.Count; index++)
        {
            var pointer = _keys.Append(_keys.Append(string.Empty, "parameters"), index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var location = string.Concat(root, pointer);
            if (rawParameters[index] is not JsonObject rawParameter)
            {
                state.Errors.Add(location, "raw parameter was not an object");
                continue;
            }

            var parameter = parameters[index];
            var errorCount = state.Errors.Count;
            var isDeepObject = _wall.Check(parameter, rawParameter, location, state.Errors);
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                state.Errors.Add(location, "parameter name is required");
            }

            if (parameter.In is not ParameterLocation.Path and not ParameterLocation.Query)
            {
                continue;
            }

            if (!identities.Add((parameter.Name ?? string.Empty, parameter.In.Value)))
            {
                state.Errors.Add(location, $"duplicate parameter '{parameter.Name}' in '{parameter.In.Value.ToString().ToLowerInvariant()}'");
            }

            if (parameter.Schema is null)
            {
                state.Errors.Add(location, "parameter schema is required");
                continue;
            }

            var schema = _schemaProjector.Project(parameter.Schema, root, _keys.Append(pointer, "schema"), state);
            if (schema is null || state.Errors.Count != errorCount)
            {
                continue;
            }

            projected.Add(new SpecParameter
            {
                Name = parameter.Name!,
                Location = parameter.In is ParameterLocation.Path ? SpecParameterLocation.Path : SpecParameterLocation.Query,
                Schema = schema,
                IsRequired = parameter.Required,
                IsDeepObject = isDeepObject,
            });
        }

        return Array.AsReadOnly([.. projected]);
    }
}
