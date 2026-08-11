using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion;

internal sealed record LoadedSpec(OpenApiDocument Document, JsonNode Raw);
