using System.Net;
using System.Net.Http.Headers;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// Answers a cursor-list traversal: the first request (no cursor) receives page 0, and a request
/// carrying <c>cursor=&lt;index&gt;</c> receives that page, so the paginator's continuation
/// requests are served deterministically without a server.
/// </summary>
internal sealed class CannedPageHandler : HttpMessageHandler
{
    private const string CursorKey = "cursor=";
    private readonly IReadOnlyList<byte[]> _pages;

    public CannedPageHandler(IReadOnlyList<byte[]> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count is 0)
        {
            throw new ArgumentException("At least one page is required.", nameof(pages));
        }

        _pages = pages;
    }

    /// <summary>The cursor value page <paramref name="index"/> advertises as its next page.</summary>
    public static string CursorFor(int index) => index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var query = request.RequestUri?.Query ?? string.Empty;
        var cursorStart = query.IndexOf(CursorKey, StringComparison.Ordinal);
        var page = 0;
        if (cursorStart >= 0)
        {
            var value = query.AsSpan(cursorStart + CursorKey.Length);
            var valueEnd = value.IndexOf('&');
            page = int.Parse((valueEnd < 0 ? value : value[..valueEnd]).ToString(), System.Globalization.CultureInfo.InvariantCulture);
        }

        var content = new ByteArrayContent(_pages[page]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
