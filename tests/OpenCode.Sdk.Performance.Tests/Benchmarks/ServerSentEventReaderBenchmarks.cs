using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// The stream frame reader in isolation: a run of event frames walked through the same
/// decoded-span line scanner and state machine the SDK reads a live body with. A reader pays for
/// decoded-character scanning/copying, line handling, and frame dispatch; one payload size cannot
/// separate them. Large frames amortize dispatch while retaining scan/copy cost, small frames emphasize
/// line and frame work, single frames expose the per-stream fixed cost, and the multi-line form measures
/// the data-line join against the one-line wire.
/// </summary>
[MemoryDiagnoser]
public class ServerSentEventReaderBenchmarks
{
    private const int SmallFrameCount = 1024;
    private const int LargeFrameCount = 64;
    private const int LinesPerMultiLineFrame = 4;
    private const int SocketChunkBytes = 1460;

    public static IEnumerable<WireFixture> Fixtures()
    {
        var large = BenchmarkFixtures.DeepAssistantMessage();
        var small = BenchmarkFixtures.SessionIdleBody();
        yield return Frames("large-x1", large, 1);
        yield return Frames("large-x64", large, LargeFrameCount);
        yield return Frames("small-x1", small, 1);
        yield return Frames("small-x1024", small, SmallFrameCount);
        yield return new WireFixture("large-x64-multiline", BenchmarkFixtures.MultiLineEventStream(large, LargeFrameCount, LinesPerMultiLineFrame),
            LargeFrameCount, payloadBytesPerItem: large.Length);
    }

    public static IEnumerable<WireFixture> ChunkedFixtures()
    {
        yield return Frames("large-x64", BenchmarkFixtures.DeepAssistantMessage(), LargeFrameCount);
        yield return Frames("small-x1024", BenchmarkFixtures.SessionIdleBody(), SmallFrameCount);
    }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        foreach (var fixture in Fixtures())
        {
            if (await ReadFramesAsync(fixture).ConfigureAwait(false) != fixture.Items)
            {
                throw new InvalidOperationException($"Fixture '{fixture.Name}' did not frame into the expected event count.");
            }
        }

        foreach (var fixture in ChunkedFixtures())
        {
            if (await ReadFramesChunkedAsync(fixture).ConfigureAwait(false) != fixture.Items)
            {
                throw new InvalidOperationException($"Chunked fixture '{fixture.Name}' did not frame into the expected event count.");
            }
        }
    }

    /// <summary>Frames one complete body, counting dispatched events.</summary>
    [Benchmark]
    [ArgumentsSource(nameof(Fixtures))]
    public async Task<int> ReadFramesAsync(WireFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        using var stream = new MemoryStream(fixture.Bytes);
        var frames = 0;
        await foreach (var frame in new ServerSentEventReader().ReadAsync(stream, CancellationToken.None).ConfigureAwait(false))
        {
            _ = frame;
            frames++;
        }

        return frames;
    }

    /// <summary>Frames one complete body delivered in socket-sized reads, so cross-read assembly is measured.</summary>
    [Benchmark]
    [ArgumentsSource(nameof(ChunkedFixtures))]
    public async Task<int> ReadFramesChunkedAsync(WireFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        using var stream = new ChunkedReadStream(fixture.Bytes, SocketChunkBytes);
        var frames = 0;
        await foreach (var frame in new ServerSentEventReader().ReadAsync(stream, CancellationToken.None).ConfigureAwait(false))
        {
            _ = frame;
            frames++;
        }

        return frames;
    }

    private static WireFixture Frames(string name, byte[] payload, int count) =>
        new(name, BenchmarkFixtures.EventStream(payload, count), count, payloadBytesPerItem: payload.Length);
}
