using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The pinned CLI's <c>service stop</c> (<c>handlers/service/stop.ts</c> over <c>Service.stop</c>,
/// <c>effect/service.ts:144-152</c>): resolve the registration the options name and run the
/// channel's migration, read it once, ask a ready and compatible daemon to shut its persistent
/// terminals down, clear the handoff sidecar through the shared <see cref="IServicePtyHandoff"/>
/// seam, then hand the registration to the terminator. A missing or corrupt registration is
/// nothing to stop; a shutdown the daemon refuses is ignored the way the CLI ignores it; the
/// caller's cancellation is the one thing that is not.
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

        var paths = new ServicePathResolver(environment).Resolve(selection);
        await new ServiceMigration(fileSystem).ApplyAsync(selection, paths, cancellationToken).ConfigureAwait(false);

        var registration = await new ServiceRegistrationFile(fileSystem)
            .TryReadAsync(paths.RegistrationFile, cancellationToken)
            .ConfigureAwait(false);
        if (registration is not null)
        {
            _ = await TryShutdownPersistentTerminalsAsync(registration, cancellationToken).ConfigureAwait(false);
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
    /// <c>ServerConnection.shutdownPersistentPty</c> (<c>server-connection.ts:65-72</c>): discover
    /// with no version filter and, when a ready and compatible daemon answers, ask it to shut the
    /// terminals down. Every failure of the exchange is the false outcome, the way the CLI's
    /// <c>Effect.ignore</c> drops it; the caller's cancellation propagates.
    /// </summary>
    private async Task<bool> TryShutdownPersistentTerminalsAsync(ServiceRegistration registration, CancellationToken cancellationToken)
    {
        var answer = await probe.ProbeAsync(registration, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (answer.State != ServiceState.Ready || !answer.Compatible)
        {
            return false;
        }

        try
        {
            await ptyShutdown.ShutdownAsync(registration, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OpenCodeException)
        {
            // Refused, failed, or unreachable: the daemon is about to be ended either way.
            return false;
        }
    }
}
