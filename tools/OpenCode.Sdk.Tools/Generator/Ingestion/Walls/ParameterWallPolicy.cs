using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal static class ParameterWallPolicy
{
    public static bool Check(IOpenApiParameter parameter, JsonObject rawParameter, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(rawParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (parameter is not OpenApiParameter concrete)
        {
            errors.Add(location, $"parameter implementation '{parameter.GetType().Name}' is not supported");
            return false;
        }

        // Every check here guards request serialization: a mislocated, content-encoded,
        // or differently styled parameter would produce a wrong request on the wire.
        if (concrete.In is not ParameterLocation.Path and not ParameterLocation.Query)
        {
            errors.Add(location, $"parameter location '{GetLocationName(concrete.In)}' is not supported");
        }

        if (concrete.Content is { Count: > 0 })
        {
            errors.Add(location, "content-based parameters are not supported");
        }

        var hasStyle = rawParameter.ContainsKey("style");
        var hasExplode = rawParameter.ContainsKey("explode");
        switch (hasStyle)
        {
            case false when !hasExplode:
                return false;
            case true when hasExplode && concrete is { Style: ParameterStyle.DeepObject, Explode: true }:
                return true;
            default:
                errors.Add(location,
                    $"parameter style '{GetStyleName(concrete.Style)}' with explode '{concrete.Explode}' is not supported; expected deepObject with true");
                return false;
        }
    }

    private static string GetLocationName(ParameterLocation? location) => location?.ToString().ToLowerInvariant() ?? "<missing>";

    private static string GetStyleName(ParameterStyle? style)
    {
        if (style is null)
        {
            return "<missing>";
        }

        return style is ParameterStyle.DeepObject ? "deepObject" : style.Value.ToString().ToLowerInvariant();
    }
}
