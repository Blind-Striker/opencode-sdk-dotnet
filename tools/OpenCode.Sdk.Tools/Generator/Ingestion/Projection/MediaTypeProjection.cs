using System.Text.Json.Nodes;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed record MediaTypeProjection(SpecMediaType ContentType, JsonNode? EffectStream);
