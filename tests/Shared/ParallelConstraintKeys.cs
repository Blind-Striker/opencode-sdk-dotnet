namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// TUnit <c>[NotInParallel]</c> constraint keys shared by the test projects.
/// </summary>
/// <remarks>
/// Two scheduling rules follow from research log Q157. Every test that starts a real server
/// process — an <c>opencode serve</c> child, a drive server, a <c>bun -e</c> stand-in, or the
/// per-session pinned fixture — carries <see cref="ServerProcess"/>, so at most one process starts
/// or shuts down at a time while the in-process suite keeps running alongside; several starting
/// together stalled the hosted Windows net472 host in ten-second slices. Every test whose
/// assertion depends on a wall-clock bound the host can miss under load — a progress-window race,
/// a <c>WaitAsync</c> on an in-process handoff, a <c>[Timeout]</c> measured in seconds — carries
/// the keyless <c>[NotInParallel]</c> instead, which TUnit runs alone after every other test.
/// </remarks>
internal static class ParallelConstraintKeys
{
    /// <summary>The single key every server-process test shares.</summary>
    public const string ServerProcess = "server-process";
}
