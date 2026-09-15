namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The validated, entry-time snapshot of a discovery request. <see cref="Channel"/> is null both
/// for the shared release default and in direct-file mode, which has no channel to name; the two
/// are told apart by <see cref="DirectRegistrationFile"/>.
/// </summary>
/// <param name="Channel">The caller-named service channel, or null for the shared release registration.</param>
/// <param name="DirectRegistrationFile">The absolute registration path that bypasses channel resolution, or null.</param>
/// <param name="InstalledVersion">The migration comparand; in channel mode it defaults from <paramref name="ExpectedVersion"/>.</param>
/// <param name="ExpectedVersion">The exact health version a discovered service must report, or null for any.</param>
internal sealed record ServiceSelection(
    string? Channel,
    string? DirectRegistrationFile,
    string? InstalledVersion,
    string? ExpectedVersion)
{
    private const string Prefix = "OpenCodeServerDiscoverOptions.";

    /// <summary>Validates the caller's options and captures their values.</summary>
    /// <param name="options">The public options; null means every default.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="ArgumentException">A value is blank, the direct file is relative, or the
    /// members contradict one another.</exception>
    public static ServiceSelection Snapshot(OpenCodeServerDiscoverOptions? options)
    {
        if (options is null)
        {
            return new ServiceSelection(null, null, null, null);
        }

        var channel = options.Channel;
        var registrationFilePath = options.RegistrationFilePath;
        var installedVersion = options.InstalledVersion;
        var expectedVersion = options.ExpectedVersion;

        if (IsBlank(channel))
        {
            throw new ArgumentException(Prefix + "Channel cannot be blank; leave it null to use the shared release registration.", nameof(options));
        }

        if (IsBlank(registrationFilePath))
        {
            throw new ArgumentException(Prefix + "RegistrationFilePath cannot be blank; leave it null to resolve the registration by channel.", nameof(options));
        }

        if (IsBlank(installedVersion))
        {
            throw new ArgumentException(Prefix + "InstalledVersion cannot be blank; leave it null to migrate by channel prefix only.", nameof(options));
        }

        if (IsBlank(expectedVersion))
        {
            throw new ArgumentException(Prefix + "ExpectedVersion cannot be blank; leave it null to accept any ready service.", nameof(options));
        }

        if (registrationFilePath is not null)
        {
            if (channel is not null)
            {
                throw new ArgumentException(Prefix + "RegistrationFilePath and Channel cannot both be set; a direct registration file has no channel.", nameof(options));
            }

            if (!Path.IsPathRooted(registrationFilePath))
            {
                throw new ArgumentException(Prefix + "RegistrationFilePath must be an absolute path.", nameof(options));
            }

            if (installedVersion is not null)
            {
                throw new ArgumentException(Prefix + "InstalledVersion has no meaning with RegistrationFilePath; direct-file discovery performs no migration.", nameof(options));
            }

            return new ServiceSelection(null, registrationFilePath, null, expectedVersion);
        }

        if (installedVersion is not null && expectedVersion is not null &&
            !string.Equals(installedVersion, expectedVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(Prefix + "InstalledVersion and ExpectedVersion must be equal when both are set; the first-party CLI carries one compiled identity.", nameof(options));
        }

        return new ServiceSelection(channel, null, installedVersion ?? expectedVersion, expectedVersion);
    }

    private static bool IsBlank(string? value) => value is not null && string.IsNullOrWhiteSpace(value);
}
