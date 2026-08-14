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
    /// Gets or sets the HTTP basic-authentication username. The upstream default is
    /// <c>opencode</c>; servers started with <c>--username</c> or
    /// <c>OPENCODE_SERVER_USERNAME</c> expect the matching value here.
    /// </summary>
    public string Username { get; set; } = "opencode";

    /// <summary>
    /// Gets or sets the HTTP basic-authentication password. <see langword="null"/> sends
    /// anonymous requests — a server without authentication configured expects none. An
    /// empty or whitespace value is refused with <see cref="ArgumentException"/> at client
    /// construction; the SDK never reads credentials from the environment.
    /// </summary>
    public string? Password { get; set; }
}
