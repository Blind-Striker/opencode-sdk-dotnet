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

    [JsonPropertyName("handleParameter")]
    public string? HandleParameter { get; init; }

    /// <summary>Why this family sits where it does; ADR-0019 owns the placement rule.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
