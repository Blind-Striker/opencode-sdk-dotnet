using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tools.Output;

internal sealed record GenerationManifest
{
    [JsonPropertyName("files")]
    public required IReadOnlyList<string> Files
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<string>());
}
