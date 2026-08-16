using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// The stream frame reader in isolation: a run of realistic event frames walked through
/// the same per-character state machine the SDK reads a live body with. This is the shape
/// the streaming half of the adapter-boundary redesign will be measured against.
/// </summary>
[MemoryDiagnoser]
public class ServerSentEventReaderBenchmarks
{
    private byte[] _eventStream = [];

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var payload = await BenchmarkFixtures.DeepAssistantMessageAsync().ConfigureAwait(false);
        _eventStream = BenchmarkFixtures.EventStream(payload, frames: 64);
    }

    [Benchmark]
    public async Task<int> ReadEventStream()
    {
        using var body = new MemoryStream(_eventStream);
        var characters = 0;
        await foreach (var frame in new ServerSentEventReader().ReadAsync(body, CancellationToken.None).ConfigureAwait(false))
        {
            characters += frame.Length;
        }

        return characters;
    }
}
