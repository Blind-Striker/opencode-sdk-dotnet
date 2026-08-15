using System.Net;

namespace OpenCode.Sdk.Extensions.Tests.Support;

/// <summary>Records every request URI and Authorization header, answering from the supplied responder.</summary>
internal sealed class RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder =
        responder ?? throw new ArgumentNullException(nameof(responder));

    private readonly List<Uri> _requestUris = [];
    private readonly List<string?> _authorizationHeaders = [];

    public IReadOnlyList<Uri> RequestUris => _requestUris;

    public IReadOnlyList<string?> AuthorizationHeaders => _authorizationHeaders;

    public static RecordingHttpHandler RespondingJson(string body) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requestUris.Add(request.RequestUri!);
        _authorizationHeaders.Add(request.Headers.Authorization?.ToString());
        return Task.FromResult(_responder(request));
    }
}
