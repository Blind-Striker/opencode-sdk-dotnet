using System.Net;
using System.Net.Http.Headers;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// Answers every request with the same 200 body and media type, so benchmarks measure the SDK
/// pipeline and serialization only — no network, no server. The per-call response/content
/// allocations are part of every measurement equally and cancel out in comparisons.
/// </summary>
internal sealed class CannedResponseHandler(byte[] body, string mediaType = "application/json") : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
