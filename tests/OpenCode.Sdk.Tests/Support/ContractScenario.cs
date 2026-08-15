using System.Net;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>Assembles an <c>OpenCodeClient</c> over a recording transport answering with one canned response.</summary>
internal sealed class ContractScenario : IDisposable
{
    public static readonly Uri Endpoint = new("http://localhost:4096");

    private readonly RecordingHttpHandler _handler;
    private readonly HttpClient _httpClient;

    private ContractScenario(RecordingHttpHandler handler)
    {
        _handler = handler;
        _httpClient = new HttpClient(handler);
        Client = new OpenCodeClient(_httpClient, new OpenCodeClientOptions
        {
            Endpoint = Endpoint,
        });
    }

    public OpenCodeClient Client { get; }

    public IReadOnlyList<RecordedRequest> Requests => _handler.Requests;

    public static ContractScenario Responding(HttpStatusCode status, string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return new(new RecordingHttpHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
        }));
    }

    public static ContractScenario Responding() => new(new RecordingHttpHandler());

    public void Dispose()
    {
        Client.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
    }
}
