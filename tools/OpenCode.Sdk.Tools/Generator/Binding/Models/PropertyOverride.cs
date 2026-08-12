using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record PropertyOverride
{
    [JsonPropertyName("schema")]
    public required string Schema { get; init; }

    [JsonPropertyName("property")]
    public required string Property { get; init; }

    [JsonPropertyName("type")]
    public required PropertyOverrideType Type { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}
