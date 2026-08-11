using System.Globalization;
using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class ResponseProjector
{
    private readonly EnvelopeClassifier _envelopes;
    private readonly GraphKeyBuilder _keys;
    private readonly MediaTypeProjector _mediaTypes;
    private readonly SchemaProjector _schemaProjector;
    private readonly ResponseWallPolicy _wall;

    public ResponseProjector(SchemaProjector schemaProjector, GraphKeyBuilder keys, ResponseWallPolicy wall, MediaTypeProjector mediaTypes,
        EnvelopeClassifier envelopes)
    {
        _schemaProjector = schemaProjector ?? throw new ArgumentNullException(nameof(schemaProjector));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _wall = wall ?? throw new ArgumentNullException(nameof(wall));
        _mediaTypes = mediaTypes ?? throw new ArgumentNullException(nameof(mediaTypes));
        _envelopes = envelopes ?? throw new ArgumentNullException(nameof(envelopes));
    }

    public IReadOnlyList<SpecResponse> Project(OpenApiResponses? responses, string root, ProjectionState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(state);

        if (responses is null)
        {
            return Array.AsReadOnly(Array.Empty<SpecResponse>());
        }

        var parsed = new List<KeyValuePair<int, IOpenApiResponse>>(responses.Count);
        var statusCodes = new HashSet<int>();
        foreach (var (key, response) in responses)
        {
            var keyLocation = string.Concat(root, _keys.Append(_keys.Append(string.Empty, "responses"), key));
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var statusCode))
            {
                state.Errors.Add(keyLocation, $"response key '{key}' must be an integer status code");
                continue;
            }

            if (!statusCodes.Add(statusCode))
            {
                state.Errors.Add(keyLocation, $"response status code '{statusCode.ToString(CultureInfo.InvariantCulture)}' is duplicated");
                continue;
            }

            parsed.Add(new KeyValuePair<int, IOpenApiResponse>(statusCode, response));
        }

        var projected = new List<SpecResponse>(parsed.Count);
        foreach (var entry in parsed.OrderBy(static entry => entry.Key))
        {
            var response = ProjectResponse(entry.Key, entry.Value, root, state);
            if (response is not null)
            {
                projected.Add(response);
            }
        }

        return Array.AsReadOnly([.. projected]);
    }

    private SpecResponse? ProjectResponse(int statusCode, IOpenApiResponse response, string root, ProjectionState state)
    {
        var status = statusCode.ToString(CultureInfo.InvariantCulture);
        var pointer = _keys.Append(_keys.Append(string.Empty, "responses"), status);
        var location = string.Concat(root, pointer);
        var errorCount = state.Errors.Count;
        if (!_wall.Check(response, location, state.Errors))
        {
            return null;
        }

        if (response.Content is not { Count: 1 } content)
        {
            return state.Errors.Count != errorCount
                ? null
                : new SpecResponse
                {
                    StatusCode = statusCode,
                    Description = response.Description,
                    ContentType = null,
                    Schema = null,
                    EnvelopeShape = _envelopes.Classify(contentType: null, schema: null, location, state.Errors),
                    IsSse = false,
                    EffectStreamJson = null,
                };
        }

        var mediaEntry = content.Single();
        var mediaPointer = _keys.Append(_keys.Append(pointer, "content"), mediaEntry.Key);
        var mediaLocation = string.Concat(root, mediaPointer);
        var media = _mediaTypes.Project(mediaEntry.Key, mediaEntry.Value, mediaLocation, state.Errors);
        if (media is null)
        {
            return null;
        }

        SchemaNode? schema = null;
        if (mediaEntry.Value.Schema is not null)
        {
            schema = _schemaProjector.Project(mediaEntry.Value.Schema, root, _keys.Append(mediaPointer, "schema"), state);
        }

        var envelope = _envelopes.Classify(media.ContentType, mediaEntry.Value.Schema, location, state.Errors);
        return state.Errors.Count != errorCount
            ? null
            : new SpecResponse
            {
                StatusCode = statusCode,
                Description = response.Description,
                ContentType = media.ContentType,
                Schema = schema,
                EnvelopeShape = envelope,
                IsSse = media.ContentType.IsEventStream,
                EffectStreamJson = media.EffectStreamJson,
            };
    }
}
