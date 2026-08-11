using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class JsonStringSchemaProjector
{
    private readonly GraphKeyBuilder _keys;
    private readonly SchemaProjector _schemaProjector;

    public JsonStringSchemaProjector(SchemaProjector schemaProjector, GraphKeyBuilder keys)
    {
        _schemaProjector = schemaProjector ?? throw new ArgumentNullException(nameof(schemaProjector));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public JsonStringNode? Project(OpenApiSchema schema, string root, string pointer, string location, ProjectionState state)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(state);

        if (!string.Equals(schema.ContentMediaType, "application/json", StringComparison.Ordinal))
        {
            state.Errors.Add(string.Concat(root, _keys.Append(pointer, "contentMediaType")), "contentMediaType must be 'application/json'");
            return null;
        }

        if (schema.ContentSchema is null)
        {
            state.Errors.Add(location, "JSON content string must declare contentSchema");
            return null;
        }

        var inner = _schemaProjector.Project(schema.ContentSchema, root, _keys.Append(pointer, "contentSchema"), state);
        return inner is null
            ? null
            : new JsonStringNode
            {
                Description = schema.Description,
                Format = schema.Format,
                Inner = inner,
            };
    }
}
