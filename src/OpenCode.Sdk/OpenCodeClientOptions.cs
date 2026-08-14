namespace OpenCode.Sdk;

/// <summary>Configures an opencode client for its whole lifetime.</summary>
public sealed class OpenCodeClientOptions
{
    /// <summary>
    /// Gets or sets the absolute HTTP or HTTPS server endpoint. Required when a caller
    /// supplies its own <c>HttpClient</c>; must stay unset when a constructor receives
    /// the endpoint directly.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the HTTP basic-authentication password for the <c>opencode</c> user.
    /// When unset (<see langword="null"/>), the <c>OPENCODE_SERVER_PASSWORD</c> environment
    /// variable is read once at client construction; an empty or whitespace value is refused
    /// with <see cref="ArgumentException"/> — the server always generates a password, so an
    /// explicitly blank one has no meaning.
    /// </summary>
    public string? Password { get; set; }
}
