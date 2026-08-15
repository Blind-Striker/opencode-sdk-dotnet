namespace OpenCode.Sdk;

/// <summary>
/// The read-only view of the client options. Consumption sites — the pipeline, the DI
/// composition — read through this contract and snapshot at construction, so an options
/// instance mutated after a client is built never changes that client's behavior.
/// </summary>
public interface IOpenCodeClientOptions
{
    /// <summary>Gets the absolute HTTP or HTTPS server endpoint; required to build a client.</summary>
    public Uri? Endpoint { get; }

    /// <summary>Gets the HTTP basic-authentication username; the pinned server accepts only the default <c>opencode</c>.</summary>
    public string Username { get; }

    /// <summary>Gets the HTTP basic-authentication password; <see langword="null"/> sends anonymous requests.</summary>
    public string? Password { get; }
}
