using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The pinned client's <c>discover</c>: resolve the registration file, run the channel's legacy
/// migration, read the registration, probe the daemon, and hand back its identity only when it is
/// ready and, if the caller asked, at the expected version. Everything unusable is "no service";
/// only refused input, the caller's cancellation, and an unresolvable home are failures.
/// </summary>
internal sealed class ServiceDiscovery(
    IServiceEnvironment environment,
    IServiceFileSystem fileSystem,
    IServiceInfoProbe probe)
{
    /// <summary>Discovers the registered daemon a selection points at.</summary>
    /// <param name="options">The caller's options; null means every default.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The ready daemon's registration, or null when there is no usable service.</returns>
    /// <exception cref="ArgumentException">The options are blank or contradictory.</exception>
    /// <exception cref="OpenCodeServerException">No user home resolves for an XDG fallback.</exception>
    public async Task<ServiceRegistration?> DiscoverAsync(OpenCodeServerDiscoverOptions? options, CancellationToken cancellationToken)
    {
        var selection = ServiceSelection.Snapshot(options);
        cancellationToken.ThrowIfCancellationRequested();

        var paths = new ServicePathResolver(environment).Resolve(selection);
        await new ServiceMigration(fileSystem).ApplyAsync(selection, paths, cancellationToken).ConfigureAwait(false);

        var bytes = await TryReadAsync(paths.RegistrationFile, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        var registration = ServiceRegistrationReader.TryRead(bytes);
        if (registration is null || registration.Password is null)
        {
            // A registration without a password cannot materialize the handle's non-null
            // credential contract; the pinned daemon always publishes one.
            return null;
        }

        var answer = await probe.ProbeAsync(registration, cancellationToken).ConfigureAwait(false);
        if (answer.State != ServiceState.Ready || !answer.Compatible)
        {
            return null;
        }

        if (selection.ExpectedVersion is not null &&
            !string.Equals(answer.Version, selection.ExpectedVersion, StringComparison.Ordinal))
        {
            return null;
        }

        return registration;
    }

    private async Task<byte[]?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            return await fileSystem.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
