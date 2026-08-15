using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// Declares two query parameters of one operation the server refuses to combine; the
/// relation lives only in the spec's prose, so it cannot be derived mechanically.
/// </summary>
internal sealed record MutuallyExclusiveQuery
{
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("parameters")]
    public required IReadOnlyList<string> Parameters
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<string>());

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}
