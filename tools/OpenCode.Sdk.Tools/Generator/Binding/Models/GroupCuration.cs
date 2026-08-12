using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record GroupCuration
{
    [JsonPropertyName("placement")]
    public required GroupPlacement Placement { get; init; }

    [JsonPropertyName("clientName")]
    public string? ClientName { get; init; }

    [JsonPropertyName("handleName")]
    public string? HandleName { get; init; }
}
