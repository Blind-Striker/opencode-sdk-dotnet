using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tools.Generator.Refresh.Models;

/// <summary>The pre-patch identity of one touched upstream file.</summary>
internal sealed record ReceiptPreimage
{
    [JsonPropertyName("path")] public required string Path { get; init; }

    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
}
