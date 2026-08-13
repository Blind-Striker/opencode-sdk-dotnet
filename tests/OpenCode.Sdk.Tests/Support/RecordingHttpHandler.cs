using System.Net;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    private readonly List<RecordedRequest> _requests = [];

    public RecordingHttpHandler()
        : this(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        })
    {
    }

    public RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);

        _responder = responder;
    }

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public bool IsDisposed { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(new RecordedRequest
        {
            RequestUri = request.RequestUri,
            Method = request.Method,
            Authorization = request.Headers.Authorization?.ToString(),
            UserAgent = request.Headers.UserAgent.Count is 0 ? null : request.Headers.UserAgent.ToString(),
            ContentType = request.Content?.Headers.ContentType?.ToString(),
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
        });
        return _responder(request);
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
