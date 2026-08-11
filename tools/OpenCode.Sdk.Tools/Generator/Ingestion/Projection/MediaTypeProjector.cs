using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class MediaTypeProjector(MediaTypeWallPolicy wall)
{
    private readonly MediaTypeWallPolicy _wall = wall ?? throw new ArgumentNullException(nameof(wall));

    public MediaTypeProjection? Project(string raw, IOpenApiMediaType media, string location, IngestionErrorCollector errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        SpecMediaType contentType;
        try
        {
            contentType = SpecMediaType.Create(raw);
        }
        catch (ArgumentException exception)
        {
            errors.Add(location, exception.Message);
            return null;
        }

        var effectStreamJson = _wall.Check(media, contentType, location, errors);
        return new MediaTypeProjection(contentType, effectStreamJson);
    }
}
