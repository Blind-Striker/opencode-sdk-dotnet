using System.Text.Json;
using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Internal.ResponseAdapters;
using OpenCode.Sdk.Internal.Serialization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// The smallest one-shot operation, decomposed so the fixed pipeline cost can be read off:
/// the complete call, the same pipeline through an adapter that materializes nothing, the
/// generated adapter alone, and source-generated materialization alone. Each row is the one
/// above it minus one layer.
/// </summary>
[MemoryDiagnoser]
public class HealthBenchmarks : IDisposable
{
    private static readonly OpenCodeClientOptions Options = new()
    {
        Endpoint = new Uri("https://benchmark.invalid"),
        Password = "benchmark",
    };

    private byte[] _body = [];
    private CannedResponseHandler? _handler;
    private HttpClient? _httpClient;
    private OpenCodeClient? _client;
    private Pipeline? _pipeline;

    public static IEnumerable<WireFixture> Fixtures()
    {
        var body = BenchmarkFixtures.HealthBody();
        yield return new WireFixture("health", body, items: 1, payloadBytesPerItem: body.Length);
    }

    [ParamsSource(nameof(Fixtures))]
    public WireFixture Fixture { get; set; } = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _body = Fixture.Bytes;
        _handler = new CannedResponseHandler(_body);
        _httpClient = new HttpClient(_handler);
        _client = new OpenCodeClient(_httpClient, Options);
        _pipeline = new Pipeline(_httpClient, ownsHttpClient: false, Options);

        var response = await GetHealthAsync().ConfigureAwait(false);
        if (response.Health is not { Healthy: true })
        {
            throw new InvalidOperationException("The health fixture did not materialize as a healthy service.");
        }
    }

    /// <summary>The complete generated operation: request, decoration, send, buffer, validate, adapt, materialize.</summary>
    [Benchmark]
    public Task<HealthResponse> GetHealthAsync() => _client!.GetHealthAsync();

    /// <summary>The same pipeline through a no-op adapter: everything above minus JSON and model cost.</summary>
    [Benchmark]
    public Task<NoOpResponse> ExecuteWithoutAdapterAsync() =>
        _pipeline!.ExecuteAsync(HttpMethod.Get, OpenCodeRoutes.Health.Get, NoOpResponseAdapter.Instance, options: null, CancellationToken.None);

    /// <summary>The generated adapter over validated UTF-8: materialization plus the response envelope.</summary>
    [Benchmark]
    public HealthResponse AdaptSuccess() => HealthResponseAdapter.Instance.AdaptSuccess(200, _body);

    /// <summary>Source-generated materialization alone.</summary>
    [Benchmark]
    public ServiceHealth? Deserialize() => JsonSerializer.Deserialize(_body, OpenCodeJsonContext.Default.ServiceHealth);

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

        _pipeline?.Dispose();
        _client?.Dispose();
        _httpClient?.Dispose();
        _handler?.Dispose();
    }
}
