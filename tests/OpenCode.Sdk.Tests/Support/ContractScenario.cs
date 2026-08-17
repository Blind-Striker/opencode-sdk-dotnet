using System.Net;
using System.Net.Http.Headers;
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

    /// <summary>Answers with a server-sent event body, the shape a streaming operation reads.</summary>
    public static ContractScenario RespondingWithFrames(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return new(new RecordingHttpHandler(_ =>
        {
            var content = new StringContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content, };
        }));
    }

    public void Dispose()
    {
        Client.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
    }
}
