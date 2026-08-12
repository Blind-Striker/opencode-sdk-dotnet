using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Output;

namespace OpenCode.Sdk.Tools.Serialization;

[JsonSourceGenerationOptions(
    AllowTrailingCommas = true,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(GenerationCuration))]
[JsonSerializable(typeof(GenerationManifest))]
internal sealed partial class ToolJsonContext : JsonSerializerContext;
