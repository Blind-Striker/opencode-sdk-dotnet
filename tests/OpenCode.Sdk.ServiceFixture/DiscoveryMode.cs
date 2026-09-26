using System.Diagnostics;
using System.Globalization;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;
using OpenCode.Sdk.Internal.BackgroundService.Discovery;
using OpenCode.Sdk.Internal.BackgroundService.Ensure;
using OpenCode.Sdk.Internal.BackgroundService.Registration;

namespace OpenCode.Sdk.ServiceFixture;

/// <summary>
/// Runs <see cref="OpenCodeServer.DiscoverAsync"/> once and prints
/// <c>found owns=&lt;true|false&gt; pid=&lt;pid&gt; endpoint=&lt;url&gt;</c> or <c>missing</c>.
/// On stderr it prints the probe's verdict — <c>probe timedOut=&lt;true|false&gt;</c>, or
/// <c>probe none</c> when discovery never reached a registered endpoint — and the elapsed time.
/// </summary>
internal static class DiscoveryMode
{
    public static async Task<int> RunAsync(OpenCodeServerDiscoverOptions? options)
    {
        try
        {
            // Discovery answers "missing" alike for no service and for a probe whose bound expired,
            // so the verdict goes to stderr beside the elapsed time, never into the stdout contract:
            // a test reads the verdict, the time is only a diagnostic.
            var probe = new RecordingProbe(new ServiceInfoProbe(ServiceTiming.Default));
            var stopwatch = Stopwatch.StartNew();
            var server = await OpenCodeServer.DiscoverWithSeamsAsync(options, probe, CancellationToken.None).ConfigureAwait(false);
            await Console.Error
                .WriteLineAsync($"discovery took {stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms")
                .ConfigureAwait(false);
            await Console.Error.WriteLineAsync(probe.Verdict).ConfigureAwait(false);
            if (server is null)
            {
                await Console.Out.WriteLineAsync("missing").ConfigureAwait(false);
                return 0;
            }

            await using (server.ConfigureAwait(false))
            {
                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"found owns={(server.OwnsProcess ? "true" : "false")} pid={server.ProcessId} endpoint={server.Endpoint}");
                await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or OpenCodeServerException)
        {
            await Console.Error.WriteLineAsync(exception.GetType().Name + ": " + exception.Message).ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>The platform probe, remembering the last verdict it gave discovery.</summary>
    private sealed class RecordingProbe(IServiceInfoProbe inner) : IServiceInfoProbe
    {
        private ServiceProbeResult? _last;

        public string Verdict
        {
            get
            {
                if (_last is null)
                {
                    return "probe none";
                }

                return _last.TimedOut ? "probe timedOut=true" : "probe timedOut=false";
            }
        }

        public async Task<ServiceProbeResult> ProbeAsync(ServiceRegistration registration, CancellationToken cancellationToken)
        {
            _last = await inner.ProbeAsync(registration, cancellationToken).ConfigureAwait(false);
            return _last;
        }
    }
}
