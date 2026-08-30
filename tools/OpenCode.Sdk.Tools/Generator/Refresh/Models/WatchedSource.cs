using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tools.Generator.Refresh.Models;

/// <summary>
/// One pinned upstream source file a hand-written door reads as its input: the path inside the
/// submodule, the SHA-256 of its blob at the accepted commit, the behavior the door depends on,
/// and the anchor that names that behavior in the file's text.
/// </summary>
internal sealed record WatchedSource
{
    /// <summary>Gets the submodule-relative path of the watched file.</summary>
    [JsonPropertyName("path")] public required string Path { get; init; }

    /// <summary>Gets the SHA-256 of the file's blob at the accepted upstream commit.</summary>
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }

    /// <summary>Gets the upstream behavior the hand-written door depends on.</summary>
    [JsonPropertyName("behavior")] public required string Behavior { get; init; }

    /// <summary>Gets the content anchor that names that behavior inside the file.</summary>
    [JsonPropertyName("anchor")] public required SourceAnchor Anchor { get; init; }
}
