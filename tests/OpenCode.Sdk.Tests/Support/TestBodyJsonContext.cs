using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tests.Support;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TestBody))]
internal sealed partial class TestBodyJsonContext : JsonSerializerContext;
