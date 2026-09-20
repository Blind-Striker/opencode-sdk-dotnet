using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The pinned CLI's legacy copy (<c>migrateRegistration</c> and <c>migrateConfig</c> in
/// <c>service-config.ts</c>): a donor written under the earlier hashed filename is copied byte for
/// byte to the current name, exclusively, once, and only when its version belongs to the channel.
/// The copy is a convenience the daemon's own registration supersedes, so every write failure is
/// swallowed the way upstream's <c>Effect.ignore</c> swallows it, no directory is created, and no
/// donor is ever removed.
/// </summary>
internal sealed class ServiceMigration(IServiceFileSystem fileSystem)
{
    private readonly ServiceRegistrationFile _registrationFile = new(fileSystem);

    /// <summary>Applies the registration and config migrations a selection calls for.</summary>
    /// <param name="selection">The validated selection; direct-file mode and the null channel migrate nothing.</param>
    /// <param name="paths">The resolved paths.</param>
    /// <param name="cancellationToken">The caller's token; cancellation is the one failure that propagates.</param>
    /// <returns>A task that completes when every applicable donor has been considered.</returns>
    public async Task ApplyAsync(ServiceSelection selection, ServicePaths paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(paths);

        if (selection.DirectRegistrationFile is not null || selection.Channel is null)
        {
            return;
        }

        foreach (var donor in paths.LegacyRegistrationFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await _registrationFile.TryReadBytesAsync(donor, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                continue;
            }

            var registration = ServiceRegistrationReader.TryRead(bytes);
            if (registration is null ||
                !VersionBelongsToChannel(registration.Version, selection.Channel, selection.InstalledVersion))
            {
                continue;
            }

            _ = await TryCopyAsync(paths.RegistrationFile, bytes, cancellationToken).ConfigureAwait(false);
        }

        if (paths.LegacyConfigFile is { } legacyConfig && paths.ConfigFile is { } configFile)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await _registrationFile.TryReadBytesAsync(legacyConfig, cancellationToken).ConfigureAwait(false);
            if (bytes is not null && ServiceConfigReader.TryReadEnvironment(bytes) is not null)
            {
                _ = await TryCopyAsync(configFile, bytes, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// <c>versionBelongsToChannel</c> at the pin: the exact installed version when the SDK was
    /// given one, otherwise the pre-release spelling <c>0.0.0-&lt;channel&gt;-N</c> or
    /// <c>0.0.0-&lt;channel&gt;-N.M</c> with decimal segments, over the raw channel string. A stable
    /// release version never matches the prefix, so a release registration is claimed only by an
    /// exact comparand.
    /// </summary>
    /// <param name="version">The donor's version, or null.</param>
    /// <param name="channel">The raw channel.</param>
    /// <param name="installedVersion">The exact comparand, or null when the SDK has none.</param>
    /// <returns>True when the donor may be copied.</returns>
    internal static bool VersionBelongsToChannel(string? version, string channel, string? installedVersion)
    {
        if (version is null)
        {
            return false;
        }

        if (installedVersion is not null && string.Equals(version, installedVersion, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = "0.0.0-" + channel + "-";
        if (!version.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return IsDecimalSegments(version.AsSpan(prefix.Length));
    }

    /// <summary><c>^\d+(?:\.\d+)?$</c> without a Regex: one run of digits, optionally one dot and a second run.</summary>
    private static bool IsDecimalSegments(ReadOnlySpan<char> rest)
    {
        var dot = rest.IndexOf('.');
        if (dot < 0)
        {
            return IsDigits(rest);
        }

        return IsDigits(rest[..dot]) && IsDigits(rest[(dot + 1)..]);
    }

    private static bool IsDigits(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> TryCopyAsync(string target, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            return await fileSystem.TryCreateExclusiveAsync(target, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Parity with Effect.ignore: a missing directory, a locked target, a full disk.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // A read-only state or config directory never fails a lookup.
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            // The one arm that cannot create the copy safely reports it this way.
            return false;
        }
    }
}
