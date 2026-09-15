using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;
using OpenCode.Sdk.Internal.Serialization;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The raw authenticated health exchange of the pinned client (<c>probeResult</c> in
/// <c>effect/service.ts</c>) over the SDK's own owned, non-redirecting handler. It does not ride the
/// pipeline: the generated health operation declares neither the daemon's 500/503 answers nor its
/// authentication, and the registration's credential must never take part in a redirect. The
/// route, the body classification, and the generated model it reuses are the parts upstream's
/// development branch has already moved; they live here and nowhere else.
/// </summary>
internal sealed class ServiceHealthProbe(ServiceTiming timing) : IServiceHealthProbe
{
    /// <summary>The pin's health route, resolved against the authority only, as <c>new URL("/api/health", info.url)</c> does.</summary>
    private const string HealthPath = "/api/health";

    private static readonly ServiceProbeResult NoService = new(State: null, Version: null, TimedOut: false);
    private static readonly ServiceProbeResult Expired = new(State: null, Version: null, TimedOut: true);

    public async Task<ServiceProbeResult> ProbeAsync(ServiceRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();

        using var client = TransportPolicy.CreateOwnedHttpClient(registration.Endpoint);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(registration.Endpoint, HealthPath));
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

        return Classify(registration, status, body);
    }

    private static ServiceProbeResult Classify(ServiceRegistration registration, HttpStatusCode status, byte[] body)
    {
        // The pre-`version` daemon answers { "healthy": true } alone. Ensure may one day classify
        // it for replacement; discovery is the allowLegacy=false path and reports no service.
        if (IsLegacyBody(body))
        {
            return NoService;
        }

        ServiceHealth? health;
        try
        {
            health = JsonSerializer.Deserialize(body, OpenCodeJsonContext.Default.ServiceHealth);
        }
        catch (JsonException)
        {
            return NoService;
        }

        // Deserializing the generated model is not the check: the model carries an Int64 pid and
        // a plain bool, and the daemon's identity is the registration's pid and version.
        if (health is null || !health.Healthy ||
            health.Pid < 0 || health.Pid > int.MaxValue || (int)health.Pid != registration.ProcessId ||
            (registration.Version is not null && !string.Equals(health.Version, registration.Version, StringComparison.Ordinal)))
        {
            return NoService;
        }

        return new ServiceProbeResult(StateOf(status), health.Version, TimedOut: false);
    }

    private static ServiceState StateOf(HttpStatusCode status) =>
        (int)status switch
        {
            >= 200 and <= 299 => ServiceState.Ready,
            (int)HttpStatusCode.InternalServerError => ServiceState.Failed,
            _ => ServiceState.Waiting,
        };

    private static bool IsLegacyBody(byte[] body)
    {
        try
        {
            var reader = new Utf8JsonReader(body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            var healthy = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("version"u8) || reader.ValueTextEquals("pid"u8))
                {
                    return false;
                }

                if (reader.ValueTextEquals("healthy"u8))
                {
                    healthy = reader.Read() && reader.TokenType == JsonTokenType.True;
                    continue;
                }

                reader.Skip();
            }

            return healthy;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
