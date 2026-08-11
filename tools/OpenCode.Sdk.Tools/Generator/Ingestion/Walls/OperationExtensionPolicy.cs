using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal static class OperationExtensionPolicy
{
    public static bool Check(OpenApiOperation operation, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        var isWebSocket = false;
        if (operation.Extensions is null)
        {
            return isWebSocket;
        }

        // Only extensions with declared generator semantics are interpreted; x-codeSamples
        // is known descriptive input and anything else is ignored — an unknown extension
        // cannot change emitted behavior, so it is not a generation failure.
        foreach (var (name, extension) in operation.Extensions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            switch (name)
            {
                case "x-websocket" when extension is JsonNodeExtension { Node: JsonValue value } && value.TryGetValue<bool>(out var flag):
                    isWebSocket = flag;
                    break;
                case "x-websocket":
                    errors.Add(location, "operation extension 'x-websocket' must be a boolean");
                    break;
                default:
                    break;
            }
        }

        return isWebSocket;
    }
}
