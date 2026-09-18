using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// Performs the pinned client's authenticated status exchange over an owned, non-redirecting
/// handler. The probe decodes only pid/version; the public generated status model has a different
/// contract. Discovery keeps its own request bound and never forwards credentials on redirects.
/// </summary>
internal sealed class ServiceStatusProbe(ServiceTiming timing) : IServiceStatusProbe
{
    /// <summary>The pin's status route, resolved against the authority only, as <c>new URL("/api/status", info.url)</c> does.</summary>
    private const string StatusPath = "/api/status";

    private static readonly ServiceProbeResult NoService = new(State: null, Version: null, TimedOut: false);
    private static readonly ServiceProbeResult Expired = new(State: null, Version: null, TimedOut: true);

    public async Task<ServiceProbeResult> ProbeAsync(ServiceRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();

        using var client = TransportPolicy.CreateOwnedHttpClient(registration.Endpoint);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(registration.Endpoint, StatusPath));
        if (registration.Password is { } password)
        {
            // UTF-8, as the pipeline encodes the same credential; upstream's probe uses btoa
            // (Latin-1) while its sidecar writer uses UTF-8, and the daemon decodes UTF-8.
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + password)));
        }

        // The internal bound and the caller's token are separate: only the caller's propagates.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(timing.RequestTimeout);

        HttpStatusCode status;
        byte[] body;
        try
        {
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bound.Token)
                .ConfigureAwait(false);
            status = response.StatusCode;
            if (status == HttpStatusCode.NotFound)
            {
                // A daemon that predates the status route answers an authenticated 404, which the
                // pinned client takes as the registered service itself, present and ready but
                // incompatible, without reading a body (service.ts:228-240 at the pin).
                return new ServiceProbeResult(ServiceState.Ready, registration.Version, TimedOut: false) { Compatible = false };
            }

            body = await response.Content.ReadAsByteArrayAsync(bound.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException)
        {
            return Expired;
        }
        catch (HttpRequestException)
        {
            return NoService;
        }
        catch (IOException)
        {
            return NoService;
        }

        return ServiceProbeResponseClassifier.Classify(registration, status, body);
    }
}
