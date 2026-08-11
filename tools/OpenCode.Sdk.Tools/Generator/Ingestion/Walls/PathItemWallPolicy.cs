using System.Collections.Frozen;
using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class PathItemWallPolicy
{
    private static readonly FrozenSet<string> AdmittedMethods = new[] { "delete", "get", "patch", "post", "put", }
        .ToFrozenSet(StringComparer.Ordinal);

    private readonly HostMemberWhitelist<OpenApiPathItem> _members = new("path item",
    [
        nameof(OpenApiPathItem.Operations),
        nameof(OpenApiPathItem.Parameters),
        nameof(OpenApiPathItem.Servers),
    ]);

    public void Check(IOpenApiPathItem pathItem, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(pathItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (pathItem is OpenApiPathItemReference)
        {
            errors.Add(location, "path item references are not supported");
            return;
        }

        if (pathItem is not OpenApiPathItem concrete)
        {
            errors.Add(location, $"path item implementation '{pathItem.GetType().Name}' is not supported");
            return;
        }

        _members.Check(concrete, location, errors);
        if (concrete.Parameters is { Count: > 0 })
        {
            errors.Add(location, "path-level parameters are not supported");
        }

        if (concrete.Servers is { Count: > 0 })
        {
            errors.Add(location, "path-level servers are not supported");
        }

        if (concrete.Operations is null)
        {
            return;
        }

        foreach (var method in concrete.Operations.Keys)
        {
            var normalized = method.Method.ToLowerInvariant();
            if (!IsAdmitted(method))
            {
                errors.Add(location, $"path item method '{normalized}' is not supported");
            }
        }
    }

    public static bool IsAdmitted(HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return AdmittedMethods.Contains(method.Method.ToLowerInvariant());
    }
}
