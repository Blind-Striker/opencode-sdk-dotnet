using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The pinned CLI's <c>service stop</c> (<c>handlers/service/stop.ts</c> over <c>Service.stop</c>,
/// <c>effect/service.ts:144-152</c>): resolve the registration the options name and run the
/// channel's migration, read it once, ask a ready and compatible daemon to shut its persistent
/// terminals down, clear the handoff sidecar, then hand the registration to the terminator. A
/// missing or corrupt registration is nothing to stop; a shutdown the daemon refuses is ignored
/// the way the CLI ignores it; the caller's cancellation is the one thing that is not.
/// </summary>
internal sealed class ServiceStopper(
    IServiceEnvironment environment,
    IServiceFileSystem fileSystem,
    IServiceInfoProbe probe,
    IServicePtyShutdown ptyShutdown,
    IServiceProcessControl processControl,
    ServiceTiming timing)
{
    /// <summary>The handoff sidecar the pinned client keeps beside the registration (<c>pty-handoff.ts</c>).</summary>
    private const string SidecarSuffix = ".pty-handoff";

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

        ClearSidecar(paths.RegistrationFile + SidecarSuffix);
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

    private void ClearSidecar(string sidecar)
    {
        try
        {
            _ = fileSystem.TryDelete(sidecar);
        }
        catch (IOException exception)
        {
            throw SidecarFailure(sidecar, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw SidecarFailure(sidecar, exception);
        }
    }

    private static OpenCodeServerException SidecarFailure(string sidecar, Exception cause) =>
        new($"The persistent-terminal handoff sidecar '{sidecar}' could not be removed, so the registered service was not stopped.", cause);
}
