namespace OpenCode.Sdk;

/// <summary>
/// Configures an opencode client for its whole lifetime. The class stays settable for the
/// options and configuration-binding patterns; clients snapshot it at construction through
/// <see cref="IOpenCodeClientOptions"/>, so later mutation never reaches a built client.
/// </summary>
public sealed class OpenCodeClientOptions : IOpenCodeClientOptions
{
    /// <summary>Gets or sets the absolute HTTP or HTTPS server endpoint; required to build a client.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the HTTP basic-authentication username; the default is
    /// <c>opencode</c>, the only username the pinned server accepts.
    /// </summary>
    public string Username { get; set; } = "opencode";

    /// <summary>
    /// Gets or sets the HTTP basic-authentication password. <see langword="null"/> sends
    /// anonymous requests — a server without authentication configured expects none. An
    /// empty or whitespace value is refused with <see cref="ArgumentException"/> at client
    /// construction; the SDK never reads credentials from the environment.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the ambient location, sent on every request as the
    /// <c>x-opencode-directory</c> and <c>x-opencode-workspace</c> headers;
    /// <see langword="null"/> leaves the server's own resolution in place. The server
    /// honors these headers only on the operations whose group resolves location from the
    /// request — operations that resolve it from a session instead, and those that do not
    /// resolve it at all, ignore them. A per-request location travels on the query channel
    /// and takes precedence <em>per member</em>: it overrides only the members it sets, and
    /// the rest still come from here.
    /// </summary>
    public LocationSelector? Location { get; set; }
}
