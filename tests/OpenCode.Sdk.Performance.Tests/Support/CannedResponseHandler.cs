using System.Net;
using System.Net.Http.Headers;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// Answers every request with the same 200 JSON body, so benchmarks measure the SDK pipeline
/// and serialization only — no network, no server. The per-call response/content allocations
/// are part of every measurement equally and cancel out in comparisons.
/// </summary>
internal sealed class CannedResponseHandler(byte[] body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
