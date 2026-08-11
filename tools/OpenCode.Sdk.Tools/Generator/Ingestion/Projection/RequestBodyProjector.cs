using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class RequestBodyProjector
{
    private readonly GraphKeyBuilder _keys;
    private readonly MediaTypeProjector _mediaTypes;
    private readonly SchemaProjector _schemaProjector;
    private readonly RequestBodyWallPolicy _wall;

    public RequestBodyProjector(SchemaProjector schemaProjector, GraphKeyBuilder keys, RequestBodyWallPolicy wall, MediaTypeProjector mediaTypes)
    {
        _schemaProjector = schemaProjector ?? throw new ArgumentNullException(nameof(schemaProjector));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _wall = wall ?? throw new ArgumentNullException(nameof(wall));
        _mediaTypes = mediaTypes ?? throw new ArgumentNullException(nameof(mediaTypes));
    }

    public SpecRequestBody? Project(IOpenApiRequestBody? requestBody, string root, ProjectionState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(state);

        if (requestBody is null)
        {
            return null;
        }

        var pointer = _keys.Append(string.Empty, "requestBody");
        var location = string.Concat(root, pointer);
        var errorCount = state.Errors.Count;
        if (!_wall.Check(requestBody, location, state.Errors) || requestBody.Content is not { Count: 1 } content)
        {
            return null;
        }

        var mediaEntry = content.Single();
        var mediaPointer = _keys.Append(_keys.Append(pointer, "content"), mediaEntry.Key);
        var mediaLocation = string.Concat(root, mediaPointer);
        var media = _mediaTypes.Project(mediaEntry.Key, mediaEntry.Value, mediaLocation, state.Errors);
        if (media is null)
        {
            return null;
        }

        if (mediaEntry.Value.Schema is null)
        {
            state.Errors.Add(mediaLocation, "request body media schema is required");
            return null;
        }

        var schema = _schemaProjector.Project(mediaEntry.Value.Schema, root, _keys.Append(mediaPointer, "schema"), state);
        return schema is null || state.Errors.Count != errorCount
            ? null
            : new SpecRequestBody
            {
                ContentType = media.ContentType,
                Schema = schema,
                IsRequired = requestBody.Required,
            };
    }
}
