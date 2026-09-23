using OpenCode.Sdk.Internal.BackgroundService.Abstractions;
using OpenCode.Sdk.Internal.Diagnostics;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The pinned CLI's <c>service stop</c> (<c>handlers/service/stop.ts</c> over <c>Service.stop</c>):
/// locate the registration the options name, read it once, ask a ready and compatible daemon to
/// shut its persistent terminals down, clear the handoff sidecar through the shared
/// <see cref="IServicePtyHandoff"/> seam, then hand the registration to the terminator. A missing
/// or corrupt registration is nothing to stop; a shutdown the daemon refuses is ignored the way the
/// CLI ignores it; the caller's cancellation is the one thing that is not.
/// </summary>
internal sealed class ServiceStopper(
    IServiceEnvironment environment,
    IServiceFileSystem fileSystem,
    IServiceInfoProbe probe,
    IServicePtyShutdown ptyShutdown,
    IServicePtyHandoff ptyHandoff,
    IServiceProcessControl processControl,
    ServiceTiming timing)
{
    /// <summary>Stops the registered service a selection points at.</summary>
    /// <param name="options">The caller's options; null means every default.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>A task that completes when the registered process is gone or there was none to stop.</returns>
    /// <exception cref="ArgumentException">The options are blank or contradictory.</exception>
    /// <exception cref="OpenCodeServerException">No user home resolves for an XDG fallback, the sidecar could not be removed, or the process survived the kill rung.</exception>
    public async Task StopAsync(OpenCodeServerStopOptions? options, CancellationToken cancellationToken)
    {
        var selection = ServiceSelection.Snapshot(options);
        cancellationToken.ThrowIfCancellationRequested();

        var paths = await new ServiceRegistrationLocator(environment, fileSystem)
            .LocateAsync(selection, cancellationToken)
            .ConfigureAwait(false);
        var registration = await ServiceRegistrationReader
            .TryReadAsync(fileSystem, paths.RegistrationFile, cancellationToken)
            .ConfigureAwait(false);
        if (registration is not null)
        {
            await ShutdownPersistentTerminalsAsync(registration, cancellationToken).ConfigureAwait(false);
        }

        await ptyHandoff.ClearAsync(paths.RegistrationFile, cancellationToken).ConfigureAwait(false);
        if (registration is not null)
        {
            await new ServiceTerminator(fileSystem, processControl, timing)
                .TerminateAsync(registration, paths.RegistrationFile, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <c>ServerConnection.shutdownPersistentPty</c> (<c>server-connection.ts:65-72</c>): when a ready
    /// and compatible daemon answers, ask it to shut the terminals down. The caller's cancellation
    /// propagates; every other failure of the exchange is dropped, the CLI's <c>Effect.ignore</c>.
    /// </summary>
    [SlopwatchSuppress(
        "SW003",
        "The pinned CLI wraps this exchange in Effect.ignore (stop.ts): a refused, failed, or unreachable shutdown changes nothing, because the daemon is ended next either way.")]
    private async Task ShutdownPersistentTerminalsAsync(ServiceRegistration registration, CancellationToken cancellationToken)
    {
        var answer = await probe.ProbeAsync(registration, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!answer.IsReadyAndCompatible)
        {
            return;
        }

        try
        {
            await ptyShutdown.ShutdownAsync(registration, cancellationToken).ConfigureAwait(false);
        }
        catch (OpenCodeException)
        {
            // Effect.ignore: the daemon is about to be ended either way.
        }
    }
}
