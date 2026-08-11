using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class RequestBodyWallPolicy
{
    private readonly HostMemberWhitelist<OpenApiRequestBody> _members = new("request body",
    [
        nameof(OpenApiRequestBody.Content),
        nameof(OpenApiRequestBody.Required),
    ]);

    public bool Check(IOpenApiRequestBody requestBody, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(requestBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (requestBody is not OpenApiRequestBody concrete)
        {
            errors.Add(location, $"request body implementation '{requestBody.GetType().Name}' is not supported");
            return false;
        }

        _members.Check(concrete, location, errors);
        if (concrete.Content is not { Count: 1 })
        {
            errors.Add(location, "request body must contain exactly one media type");
            return false;
        }

        return true;
    }
}
