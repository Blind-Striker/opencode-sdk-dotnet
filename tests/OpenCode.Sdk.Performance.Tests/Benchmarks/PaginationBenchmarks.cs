using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// The automatic cursor traversal against its explicit-page control over the same three canned
/// pages, so the hand-written paginator's own cost — continuation requests, adapter projection,
/// and the async iterator — is the difference between the two rows.
/// </summary>
[MemoryDiagnoser]
public class PaginationBenchmarks : IDisposable
{
    private const int PageCount = 3;
    private const int ItemsPerPage = 10;

    private static readonly OpenCodeClientOptions Options = new()
    {
        Endpoint = new Uri("https://benchmark.invalid"),
        Password = "benchmark",
    };

    private CannedPageHandler? _handler;
    private HttpClient? _httpClient;
    private OpenCodeClient? _client;
    private SessionClient? _session;

    public static IEnumerable<WireFixture> Fixtures()
    {
        var pages = Pages();
        var composer = new AssistantMessageComposer(BenchmarkFixtures.DeepAssistantMessage());
        yield return new WireFixture("3-pages-x-10", [.. pages.SelectMany(static page => page)], PageCount * ItemsPerPage,
            payloadBytesPerItem: composer.MarkerEarly().Length);
    }

    [ParamsSource(nameof(Fixtures))]
    public WireFixture Fixture { get; set; } = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _handler = new CannedPageHandler(Pages());
        _httpClient = new HttpClient(_handler);
        _client = new OpenCodeClient(_httpClient, Options);
        _session = _client.Sessions.GetSessionClient("ses_bench0000000000000000001");

        if (await EnumerateMessagesAsync().ConfigureAwait(false) != PageCount * ItemsPerPage
            || await ListPagesManuallyAsync().ConfigureAwait(false) != PageCount * ItemsPerPage)
        {
            throw new InvalidOperationException("The canned pages did not traverse to the expected item count.");
        }
    }

    /// <summary>The generated lazy item sequence following each opaque next cursor.</summary>
    [Benchmark]
    public async Task<int> EnumerateMessagesAsync()
    {
        var session = _session!;
        var count = 0;
        await foreach (var item in session.EnumerateMessagesAsync().ConfigureAwait(false))
        {
            _ = item;
            count++;
        }

        return count;
    }

    /// <summary>The control: the same pages through explicit page calls and a hand-written loop.</summary>
    [Benchmark]
    public async Task<int> ListPagesManuallyAsync()
    {
        var session = _session!;
        var count = 0;
        MessageListRequest? request = null;
        while (true)
        {
            var page = await session.ListMessagesAsync(request).ConfigureAwait(false);
            foreach (var item in page.Messages)
            {
                _ = item;
                count++;
            }

            if (page.Cursor.Next is not { } next)
            {
                return count;
            }

            request = new MessageListRequest
            {
                Cursor = next,
            };
        }
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

    private static byte[][] Pages()
    {
        var composer = new AssistantMessageComposer(BenchmarkFixtures.DeepAssistantMessage());
        var pages = new byte[PageCount][];
        for (var index = 0; index < PageCount; index++)
        {
            var last = index == PageCount - 1;
            pages[index] = BenchmarkFixtures.CursorListEnvelope(composer.Page(ItemsPerPage), last ? null : CannedPageHandler.CursorFor(index + 1));
        }

        return pages;
    }
}
