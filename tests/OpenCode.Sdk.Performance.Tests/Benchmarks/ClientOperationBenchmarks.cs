using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// End-to-end operation cost through the real pipeline over a canned in-memory response:
/// request build, auth decoration, buffering, adapter, and the full deserialization chain.
/// Baselines for the envelope single-pass (P2), success-path buffering (P3), and the
/// converter walk they feed (P1); issues #18 and #23.
/// </summary>
[MemoryDiagnoser]
public class ClientOperationBenchmarks : IDisposable
{
    private CannedResponseHandler? _messageHandler;
    private CannedResponseHandler? _healthHandler;
    private CannedResponseHandler? _messageListHandler;
    private HttpClient? _messageHttpClient;
    private HttpClient? _healthHttpClient;
    private HttpClient? _messageListHttpClient;
    private OpenCodeClient? _messageClient;
    private OpenCodeClient? _healthClient;
    private OpenCodeClient? _messageListClient;
    private SessionClient? _session;
    private SessionClient? _messageListSession;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var options = new OpenCodeClientOptions
        {
            Endpoint = new Uri("https://benchmark.invalid"),
            Password = "benchmark",
        };

        var envelope = BenchmarkFixtures.DataEnvelope(await BenchmarkFixtures.DeepAssistantMessageAsync().ConfigureAwait(false));
        _messageHandler = new CannedResponseHandler(envelope);
        _messageHttpClient = new HttpClient(_messageHandler);
        _messageClient = new OpenCodeClient(_messageHttpClient, options);
        _session = _messageClient.Sessions.GetSessionClient("ses_bench0000000000000000001");

        _healthHandler = new CannedResponseHandler(BenchmarkFixtures.HealthBody());
        _healthHttpClient = new HttpClient(_healthHandler);
        _healthClient = new OpenCodeClient(_healthHttpClient, options);

        var page = BenchmarkFixtures.CursorListEnvelope(await BenchmarkFixtures.DeepAssistantMessageAsync().ConfigureAwait(false));
        _messageListHandler = new CannedResponseHandler(page);
        _messageListHttpClient = new HttpClient(_messageListHandler);
        _messageListClient = new OpenCodeClient(_messageListHttpClient, options);
        _messageListSession = _messageListClient.Sessions.GetSessionClient("ses_bench0000000000000000001");
    }

    [Benchmark]
    public Task<SessionMessageResponse> GetMessageAsync() => _session!.GetMessageAsync("msg_bench00000000000000000001");

    [Benchmark]
    public Task<HealthResponse> GetHealthAsync() => _healthClient!.GetHealthAsync();

    [Benchmark]
    public Task<MessageListResponse> ListMessagesAsync() => _messageListSession!.ListMessagesAsync();

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

        _messageClient?.Dispose();
        _healthClient?.Dispose();
        _messageListClient?.Dispose();
        _messageHttpClient?.Dispose();
        _healthHttpClient?.Dispose();
        _messageListHttpClient?.Dispose();
        _messageHandler?.Dispose();
        _healthHandler?.Dispose();
        _messageListHandler?.Dispose();
    }
}
