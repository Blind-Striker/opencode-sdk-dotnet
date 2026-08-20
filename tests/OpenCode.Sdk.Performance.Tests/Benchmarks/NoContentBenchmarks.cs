using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// A declared no-content success: the pipeline sends, reads the status, and disposes without
/// touching a body, so this is the floor every one-shot operation stands on — request
/// construction, decoration, the canned handler, the status walls, and response ownership. The
/// raw send row is the harness itself (HttpClient plus the canned handler) with no SDK at all.
/// </summary>
[MemoryDiagnoser]
public class NoContentBenchmarks : IDisposable
{
    private static readonly OpenCodeClientOptions Options = new()
    {
        Endpoint = new Uri("https://benchmark.invalid"),
        Password = "benchmark",
    };

    private static readonly Uri RawUri = new("https://benchmark.invalid/api/session/ses_bench0000000000000000001");

    private CannedResponseHandler? _handler;
    private HttpClient? _httpClient;
    private OpenCodeClient? _client;
    private SessionClient? _session;

    public static IEnumerable<WireFixture> Fixtures()
    {
        yield return new WireFixture("no-content", [], items: 0, payloadBytesPerItem: 0);
    }

    [ParamsSource(nameof(Fixtures))]
    public WireFixture Fixture { get; set; } = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _handler = CannedResponseHandler.NoContent();
        _httpClient = new HttpClient(_handler);
        _client = new OpenCodeClient(_httpClient, Options);
        _session = _client.Sessions.GetSessionClient("ses_bench0000000000000000001");

        var response = await RemoveSessionAsync().ConfigureAwait(false);
        if (response.Status is not 204)
        {
            throw new InvalidOperationException("The no-content fixture did not answer with the declared 204.");
        }
    }

    /// <summary>The complete generated no-content operation.</summary>
    [Benchmark]
    public Task<SessionRemoveResponse> RemoveSessionAsync() => _session!.RemoveSessionAsync();

    /// <summary>The harness floor: the same canned handler through a bare <see cref="HttpClient"/> send, no SDK.</summary>
    [Benchmark]
    public async Task<int> SendRawRequestAsync()
    {
        var httpClient = _httpClient!;
        using var request = new HttpRequestMessage(HttpMethod.Delete, RawUri);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

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

        _client?.Dispose();
        _httpClient?.Dispose();
        _handler?.Dispose();
    }
}
