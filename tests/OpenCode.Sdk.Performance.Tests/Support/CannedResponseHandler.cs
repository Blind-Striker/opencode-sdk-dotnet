using System.Net;
using System.Net.Http.Headers;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// Answers every request with the same status, body, and media type, so benchmarks measure the SDK
/// pipeline and serialization only — no network, no server. The per-call response/content
/// allocations are part of every measurement equally and cancel out in comparisons; the
/// pipeline-overhead controls expose their size.
/// </summary>
internal sealed class CannedResponseHandler : HttpMessageHandler
{
    private readonly byte[]? _body;
    private readonly string _mediaType;
    private readonly HttpStatusCode _status;

    public CannedResponseHandler(byte[] body, string mediaType = "application/json", string? charset = null,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        _body = body;
        _mediaType = charset is null ? mediaType : $"{mediaType}; charset={charset}";
        _status = status;
    }

    private CannedResponseHandler(HttpStatusCode status)
    {
        _mediaType = "application/json";
        _status = status;
    }

    /// <summary>A handler answering a declared no-content success with no body at all.</summary>
    public static CannedResponseHandler NoContent() => new(HttpStatusCode.NoContent);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_status);
        if (_body is not null)
        {
            var content = new ByteArrayContent(_body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(_mediaType);
            response.Content = content;
        }

        return Task.FromResult(response);
    }
}
