using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tests.Support;

internal sealed record TestBody
{
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}
