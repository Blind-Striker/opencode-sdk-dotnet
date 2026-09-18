using System.Net;
using System.Text.Json;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>Classifies the status probe using the pinned client's pid/version decoder, independently of the public status model.</summary>
internal static class ServiceProbeResponseClassifier
{
    private static readonly ServiceProbeResult NotThisDaemon = new(State: null, Version: null, TimedOut: false);

    public static ServiceProbeResult Classify(ServiceRegistration registration, HttpStatusCode status, ReadOnlyMemory<byte> body)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (ReadIdentity(body) is not { } identity || identity.ProcessId != registration.ProcessId)
        {
            return NotThisDaemon;
        }

        if (registration.Version is not null && !string.Equals(identity.Version, registration.Version, StringComparison.Ordinal))
        {
            return NotThisDaemon;
        }

        var state = (int)status switch
        {
            >= 200 and <= 299 => ServiceState.Ready,
            (int)HttpStatusCode.InternalServerError => ServiceState.Failed,
            _ => ServiceState.Waiting,
        };
        return new ServiceProbeResult(state, identity.Version, TimedOut: false);
    }

    /// <summary>Decodes the two identity fields the pinned client requires; anything else is not an identity.</summary>
    private static ServiceProbeIdentity? ReadIdentity(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("pid", out var pid)
                || pid.ValueKind != JsonValueKind.Number
                || !pid.TryGetInt32(out var processId)
                || processId < 0
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            // JsonDocument retains a malformed Unicode string until GetString decodes it, which
            // is the one InvalidOperationException this decode can raise.
            return new ServiceProbeIdentity(processId, version.GetString()!);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record ServiceProbeIdentity(int ProcessId, string Version);
}
