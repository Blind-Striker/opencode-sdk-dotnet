using System.Text.Json;
using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Internal.Serialization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// The nested union converter walk in isolation (message → assistant content → tool state →
/// tool content), through the same source-generated dispatch the SDK uses.
/// </summary>
[MemoryDiagnoser]
public class UnionDeserializationBenchmarks
{
    private byte[] _deepAssistantMessage = [];

    [GlobalSetup]
    public async Task SetupAsync() => _deepAssistantMessage = await BenchmarkFixtures.DeepAssistantMessageAsync().ConfigureAwait(false);

    [Benchmark]
    public ISessionMessageInfo? DeserializeDeepAssistantMessage()
        => JsonSerializer.Deserialize(_deepAssistantMessage, OpenCodeJsonContext.Default.ISessionMessageInfo);
}
