namespace OpenCode.Sdk;

/// <summary>
/// Configures how <see cref="OpenCodeServer.StartAsync"/> launches a standalone server. The
/// class stays settable for the options pattern; the start snapshots every member, so later
/// mutation never reaches a started server.
/// </summary>
public sealed class OpenCodeServerOptions
{
    /// <summary>
    /// Gets or sets the server command: the executable followed by its leading arguments. The
    /// launcher appends <c>--stdio --port 0</c> — the reference client's exact standalone argv.
    /// The default runs <c>opencode serve</c> from the PATH, upstream's own default command
    /// shape; tests and tools point this at a source-run instead.
    /// </summary>
    public IReadOnlyList<string> Command { get; set; } = ["opencode", "serve"];

    /// <summary>Gets or sets the child's working directory; null inherits the caller's.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets extra environment entries for the child. The launcher writes its own
    /// generated <c>OPENCODE_PASSWORD</c> entry after these, so a supplied value can never
    /// shadow the lease credential.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Gets or sets how long the start waits for the JSON readiness line before failing and
    /// ending the child; must be positive. The default is 60 seconds — source-run servers boot
    /// slowly on cold CI runners.
    /// </summary>
    public TimeSpan ReadinessTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the grace between the stdin-EOF lease release and the forced tree kill;
    /// zero means immediate escalation. The default mirrors the reference client's 3-second
    /// force-kill window.
    /// </summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(3);
}
