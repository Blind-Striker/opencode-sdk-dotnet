using System.Net.Http.Headers;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// The construction-time connection facts a door outside the HTTP pipeline needs to address the
/// same server the pipeline addresses. The PTY WebSocket upgrade is that door: it builds its own
/// socket, so it cannot reach the endpoint the pipeline holds privately or the credential the
/// decoration policy holds internally. The snapshot is read once at construction exactly as the
/// policies' own snapshot is, so mutating the options object afterwards never changes a built
/// client.
/// </summary>
/// <param name="EndpointBase">The normalized request base: scheme, authority, and path prefix without a trailing slash.</param>
/// <param name="Authorization">The Basic credential every request carries, or null for a server without authentication.</param>
/// <param name="Location">The ambient location a per-call selector merges over member by member.</param>
internal sealed record ConnectionSnapshot(
    string EndpointBase,
    AuthenticationHeaderValue? Authorization,
    LocationSelector? Location);
