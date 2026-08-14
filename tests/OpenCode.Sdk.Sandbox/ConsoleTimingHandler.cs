using System.Diagnostics;
using System.Globalization;

namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// A consumer-owned delegating handler riding the factory chain — the third extensibility
/// rung in action: the SDK ships no middleware, the ecosystem seam carries it.
/// </summary>
internal sealed class ConsoleTimingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  [http] {request.Method} {request.RequestUri?.PathAndQuery} -> {(int)response.StatusCode} ({stopwatch.ElapsedMilliseconds} ms)"));
        return response;
    }
}
