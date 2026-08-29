namespace OpenCode.Sdk.Internal;

/// <summary>
/// The two bounds every <see cref="TerminalSocketCore{TFrame}"/> reads and closes under. They
/// live on a non-generic type deliberately: a static field on a generic type is one field per
/// closed construction (S2743), which would silently make a shared protocol bound per-family.
/// </summary>
internal static class TerminalSocketBounds
{
    /// <summary>
    /// The replay is chunked at 64Ki UTF-16 code units, so one message can reach roughly 192 KiB
    /// of UTF-8. Receiving it in fixed slices keeps the per-session buffer small; the read loop
    /// assembles the fragments.
    /// </summary>
    public const int ReceiveBufferSize = 16 * 1024;

    /// <summary>How long a graceful close may take before disposal stops waiting on the peer.</summary>
    public static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(5);
}
