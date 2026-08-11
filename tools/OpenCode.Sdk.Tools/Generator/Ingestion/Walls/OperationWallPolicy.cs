using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class OperationWallPolicy
{
    private readonly HostMemberWhitelist<OpenApiOperation> _members = new("operation",
    [
        nameof(OpenApiOperation.Deprecated),
        nameof(OpenApiOperation.Description),
        nameof(OpenApiOperation.Extensions),
        nameof(OpenApiOperation.Metadata),
        nameof(OpenApiOperation.OperationId),
        nameof(OpenApiOperation.Parameters),
        nameof(OpenApiOperation.RequestBody),
        nameof(OpenApiOperation.Responses),
        nameof(OpenApiOperation.Security),
        nameof(OpenApiOperation.Summary),
        nameof(OpenApiOperation.Tags),
    ]);

    public bool Check(OpenApiOperation operation, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        _members.Check(operation, location, errors);
        var isWebSocket = false;
        if (operation.Extensions is null)
        {
            return isWebSocket;
        }

        foreach (var (name, extension) in operation.Extensions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            switch (name)
            {
                case "x-codeSamples":
                    break;
                case "x-websocket" when extension is JsonNodeExtension { Node: JsonValue value } && value.TryGetValue<bool>(out var flag):
                    isWebSocket = flag;
                    break;
                case "x-websocket":
                    errors.Add(location, "operation extension 'x-websocket' must be a boolean");
                    break;
                default:
                    errors.Add(location, $"operation extension '{name}' is not supported");
                    break;
            }
        }

        return isWebSocket;
    }
}
