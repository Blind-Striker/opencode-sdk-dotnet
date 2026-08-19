using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tests.Support;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TestBody))]
[JsonSerializable(typeof(TestStreamFailureCause[]))]
internal sealed partial class TestBodyJsonContext : JsonSerializerContext;
