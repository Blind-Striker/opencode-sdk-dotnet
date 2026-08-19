using System.Text.Json.Serialization;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

internal sealed record TestStreamFailureCause : IOpenCodeStreamFailureCause
{
    [JsonPropertyName("tag")] public required string Tag { get; init; }

    [JsonPropertyName("detail")] public required string Detail { get; init; }
}
