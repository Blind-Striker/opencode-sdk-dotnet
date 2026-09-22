namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The CLI's version-mismatch wrapper over Ensure, kept pure: Ignore strips the expected version
/// before the loop; Replace keeps it; Error decides from two Discover results without entering
/// the loop on a mismatch. The loop itself only sees <see cref="Matches"/>.
/// </summary>
internal sealed class ServiceVersionPolicy
{
    /// <summary>Initializes the policy from the public options.</summary>
    /// <param name="kind">The public mismatch policy.</param>
    /// <param name="expectedVersion">The exact version to match, or null for any.</param>
    public ServiceVersionPolicy(OpenCodeServerVersionPolicy kind, string? expectedVersion)
    {
        Kind = kind;
        ExpectedVersion = expectedVersion;
    }

    /// <summary>Gets the public mismatch policy.</summary>
    public OpenCodeServerVersionPolicy Kind { get; }

    /// <summary>Gets the caller-supplied expected version, before Ignore strips it.</summary>
    public string? ExpectedVersion { get; }

    /// <summary>
    /// Gets the version the election loop filters with: null when Ignore strips it, otherwise
    /// <see cref="ExpectedVersion"/>.
    /// </summary>
    public string? LoopVersion => Kind == OpenCodeServerVersionPolicy.Ignore ? null : ExpectedVersion;

    /// <summary>Gets a value indicating whether Error's two Discover calls run before the loop.</summary>
    public bool RequiresPreamble => Kind == OpenCodeServerVersionPolicy.Error;

    /// <summary>The pinned client's <c>matchesVersion</c>: none expected matches all.</summary>
    /// <param name="reported">The version the probe reported, or null.</param>
    /// <returns>True when the loop should treat this service as version-compatible.</returns>
    public bool Matches(string? reported) => MatchesVersion(reported, LoopVersion);

    /// <summary>The pinned client's <c>matchesVersion</c> against an explicit expected value.</summary>
    /// <param name="reported">The version the probe reported, or null.</param>
    /// <param name="expected">The required version, or null for any.</param>
    /// <returns>True when they match, or when nothing is expected.</returns>
    public static bool MatchesVersion(string? reported, string? expected)
    {
        if (expected is null)
        {
            return true;
        }

        if (reported is null)
        {
            return false;
        }

        return string.Equals(reported, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Error's two Discover calls, already performed: a version-matching service is returned, a
    /// service at any version is a mismatch, and nothing registered enters the loop.
    /// </summary>
    /// <param name="compatible">The Discover result with the expected version applied.</param>
    /// <param name="existing">The Discover result with the version filter stripped.</param>
    /// <returns>The preamble decision.</returns>
    public static ServiceVersionPreamble DecideErrorPreamble(
        ServiceRegistration? compatible,
        ServiceRegistration? existing)
    {
        if (compatible is not null)
        {
            return new ServiceVersionPreamble.ReturnExisting(compatible);
        }

        if (existing is not null)
        {
            return new ServiceVersionPreamble.ThrowMismatch();
        }

        return new ServiceVersionPreamble.EnterLoop();
    }
}
