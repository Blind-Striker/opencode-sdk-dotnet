namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// A side-effect the election iteration requests. The ensurer executes these in order; the
/// iteration itself performs no I/O.
/// </summary>
internal abstract record ServiceElectionEffect
{
    private ServiceElectionEffect()
    {
    }

    /// <summary>Fire <c>onStart("missing")</c> unless it has already fired.</summary>
    internal sealed record AnnounceMissing : ServiceElectionEffect;

    /// <summary>Fire <c>onStart("version-mismatch", previousVersion)</c> unless it has already fired.</summary>
    /// <param name="PreviousVersion">The version the incompatible service reported.</param>
    internal sealed record AnnounceVersionMismatch(string? PreviousVersion) : ServiceElectionEffect;

    /// <summary>Clear the handoff sidecar.</summary>
    internal sealed record ClearHandoff : ServiceElectionEffect;

    /// <summary>Terminate the named registration the way the pinned client's <c>terminate</c> does.</summary>
    /// <param name="Registration">The registration to end.</param>
    internal sealed record Terminate(ServiceRegistration Registration) : ServiceElectionEffect;

    /// <summary>
    /// The incompatible-service stop: prepare-or-clear then terminate, swallowed as one unit the
    /// way upstream's <c>stop(...).catch(() =&gt; undefined)</c> is.
    /// </summary>
    /// <param name="Registration">The incompatible service.</param>
    /// <param name="PrepareHandoff">True when the daemon is ready, so persistent terminals can be handed off; false clears.</param>
    internal sealed record ReplaceIncompatible(ServiceRegistration Registration, bool PrepareHandoff) : ServiceElectionEffect;

    /// <summary>Spawn one contender when fewer than two are live and the spawn delay has elapsed.</summary>
    internal sealed record Spawn : ServiceElectionEffect;
}
