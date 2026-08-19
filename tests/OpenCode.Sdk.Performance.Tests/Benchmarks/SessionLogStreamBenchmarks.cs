using System.Globalization;
using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// End-to-end session-log stream cost over a canned in-memory event-stream response. Each
/// operation crosses the real request pipeline, SSE frame reader, generated stream adapter,
/// and source-generated union converter. The two arms cover high-count, shallow watermarks and
/// lower-count, larger durable events; they are distinct workload baselines, not causal isolation
/// of framing, payload size, or model depth.
/// </summary>
[MemoryDiagnoser]
public class SessionLogStreamBenchmarks : IDisposable
{
    private const int LargeFrameCount = 64;
    private const int LargeTitleCharacters = 2048;
    private const int SmallFrameCount = 1024;

    private CannedResponseHandler? _largeHandler;
    private HttpClient? _largeHttpClient;
    private OpenCodeClient? _largeClient;
    private SessionClient? _largeSession;
    private CannedResponseHandler? _smallHandler;
    private HttpClient? _smallHttpClient;
    private OpenCodeClient? _smallClient;
    private SessionClient? _smallSession;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var options = new OpenCodeClientOptions
        {
            Endpoint = new Uri("https://benchmark.invalid"),
            Password = "benchmark",
        };

        var largeBody = BenchmarkFixtures.EventStream(
            BenchmarkFixtures.LargeSessionCreatedBody(LargeTitleCharacters),
            LargeFrameCount);
        _largeHandler = new CannedResponseHandler(largeBody, "text/event-stream");
        _largeHttpClient = new HttpClient(_largeHandler);
        _largeClient = new OpenCodeClient(_largeHttpClient, options);
        _largeSession = _largeClient.Sessions.GetSessionClient("ses_bench");

        var smallBody = BenchmarkFixtures.EventStream(BenchmarkFixtures.SessionLogSyncedBody(), SmallFrameCount);
        _smallHandler = new CannedResponseHandler(smallBody, "text/event-stream");
        _smallHttpClient = new HttpClient(_smallHandler);
        _smallClient = new OpenCodeClient(_smallHttpClient, options);
        _smallSession = _smallClient.Sessions.GetSessionClient("ses_bench");

        await ValidateAsync(_largeSession, typeof(SessionCreated), LargeFrameCount).ConfigureAwait(false);
        await ValidateAsync(_smallSession, typeof(EventLogSynced), SmallFrameCount).ConfigureAwait(false);
    }

    [Benchmark]
    public Task<int> ReadLargeFramesAsync() => ReadAsync(_largeSession!);

    [Benchmark]
    public Task<int> ReadSmallFramesAsync() => ReadAsync(_smallSession!);

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _largeClient?.Dispose();
        _smallClient?.Dispose();
        _largeHttpClient?.Dispose();
        _smallHttpClient?.Dispose();
        _largeHandler?.Dispose();
        _smallHandler?.Dispose();
    }

    private static async Task<int> ReadAsync(SessionClient session)
    {
        var count = 0;
        await foreach (var item in session.GetLogAsync().ConfigureAwait(false))
        {
            _ = item;
            count++;
        }

        return count;
    }

    private static async Task ValidateAsync(SessionClient session, Type expectedType, int expectedCount)
    {
        var count = 0;
        await foreach (var item in session.GetLogAsync().ConfigureAwait(false))
        {
            if (item.GetType() != expectedType)
            {
                throw new InvalidOperationException(
                    $"Expected stream item type '{expectedType.Name}', but materialized '{item.GetType().Name}'.");
            }

            count++;
        }

        if (count != expectedCount)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Expected {expectedCount} stream items, but materialized {count}."));
        }
    }
}
