using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class MediaTypeWallPolicy
{
    private readonly HostMemberWhitelist<OpenApiMediaType> _members = new("media type",
    [
        nameof(OpenApiMediaType.Extensions),
        nameof(OpenApiMediaType.Schema),
    ]);

    public string? Check(IOpenApiMediaType media, SpecMediaType contentType, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (media is not OpenApiMediaType concrete)
        {
            errors.Add(location, $"media type implementation '{media.GetType().Name}' is not supported");
            return null;
        }

        _members.Check(concrete, location, errors);
        string? effectStreamJson = null;
        if (concrete.Extensions is null)
        {
            return effectStreamJson;
        }

        foreach (var (name, extension) in concrete.Extensions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (string.Equals(name, "x-effect-stream", StringComparison.Ordinal) && contentType.IsEventStream)
            {
                if (extension is JsonNodeExtension json)
                {
                    effectStreamJson = json.Node.ToJsonString();
                }
                else
                {
                    errors.Add(location, "media extension 'x-effect-stream' must contain JSON");
                }
            }
            else if (string.Equals(name, "x-effect-stream", StringComparison.Ordinal))
            {
                errors.Add(location, "media extension 'x-effect-stream' is supported only on text/event-stream");
            }
            else
            {
                errors.Add(location, $"media extension '{name}' is not supported");
            }
        }

        return effectStreamJson;
    }
}
