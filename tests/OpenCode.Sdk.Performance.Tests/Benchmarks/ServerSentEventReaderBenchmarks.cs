using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// The stream frame reader in isolation: a run of event frames walked through the same
/// per-character state machine the SDK reads a live body with. Two runs, because a reader
/// has a per-character cost and a per-frame cost and one payload size cannot separate them:
/// large frames amortize the per-frame work away, small frames are dominated by it, and a
/// live event feed carries mostly small ones.
/// </summary>
[MemoryDiagnoser]
public class ServerSentEventReaderBenchmarks
{
    private const int SmallFrameCount = 1024;
    private const int LargeFrameCount = 64;

    private byte[] _largeFrames = [];
    private byte[] _smallFrames = [];

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var payload = await BenchmarkFixtures.DeepAssistantMessageAsync().ConfigureAwait(false);
        _largeFrames = BenchmarkFixtures.EventStream(payload, LargeFrameCount);
        _smallFrames = BenchmarkFixtures.EventStream(BenchmarkFixtures.SessionIdleBody(), SmallFrameCount);
    }

    [Benchmark]
    public async Task<int> ReadLargeFrames() => await ReadAsync(_largeFrames).ConfigureAwait(false);

    [Benchmark]
    public async Task<int> ReadSmallFrames() => await ReadAsync(_smallFrames).ConfigureAwait(false);

    private static async Task<int> ReadAsync(byte[] body)
    {
        using var stream = new MemoryStream(body);
        var characters = 0;
        await foreach (var frame in new ServerSentEventReader().ReadAsync(stream, CancellationToken.None)
                           .ConfigureAwait(false))
        {
            characters += frame.Data.Length;
        }

        return characters;
    }
}
