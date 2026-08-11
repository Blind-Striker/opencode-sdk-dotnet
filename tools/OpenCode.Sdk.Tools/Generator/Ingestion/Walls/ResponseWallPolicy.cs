using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class ResponseWallPolicy
{
    private readonly HostMemberWhitelist<OpenApiResponse> _members = new("response",
    [
        nameof(OpenApiResponse.Content),
        nameof(OpenApiResponse.Description),
    ]);

    public bool Check(IOpenApiResponse response, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (response is not OpenApiResponse concrete)
        {
            errors.Add(location, $"response implementation '{response.GetType().Name}' is not supported");
            return false;
        }

        _members.Check(concrete, location, errors);
        if (concrete.Content is { Count: > 1 })
        {
            errors.Add(location, "response content supports at most one media type");
            return false;
        }

        return true;
    }
}
