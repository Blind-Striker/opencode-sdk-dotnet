using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk;

/// <summary>
/// The 'PersistentPtysClient' collection client. Every public door of the persistent PTY family
/// is hand-written over the generated internal raw clients (ADR-0021): the family's center of
/// gravity is a daemon-owned terminal worked through a live WebSocket session and a token
/// handshake whose knowledge the pinned document does not carry, so the surface is owned here
/// while route, status, and schema drift still breaks compilation through
/// <see cref="PersistentPtysRawClient"/>. The session-keyed operations take the session id as an
/// argument, exactly as upstream flattens the group.
/// </summary>
public class PersistentPtysClient
{
    private readonly PersistentPtysRawClient? _raw;

    internal PersistentPtysClient(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _raw = new PersistentPtysRawClient(pipeline);
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PersistentPtysClient()
    {
    }

    /// <summary>
    /// Gets a bound 'PersistentPtyClient'; the handle never caches server state.
    /// </summary>
    /// <param name="ptyId">The 'ptyID' route value.</param>
    /// <returns>The bound 'PersistentPtyClient'.</returns>
    public virtual PersistentPtyClient GetPersistentPtyClient(string ptyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ptyId);
        if (ptyId is "." or "..")
        {
            throw new ArgumentException("Route values must not be dot segments.", nameof(ptyId));
        }

        return new PersistentPtyClient(Raw.GetPersistentPtyRawClient(ptyId));
    }

    /// <summary>
    /// List the session's persistent terminals. Answers an empty list when the opencode-pty
    /// daemon is not running.
    /// </summary>
    /// <param name="sessionId">The 'sessionID' route value.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtyListResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtyListResponse> ListPersistentPtysAsync(string sessionId,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.ListPersistentPtysAsync(sessionId, requestOptions, cancellationToken);

    /// <summary>
    /// Create a persistent terminal for the session. This is the one operation that starts the
    /// opencode-pty daemon; on a platform without the daemon it answers the declared 503
    /// <see cref="ServiceUnavailableError"/> whose service is <c>opencode-pty</c>.
    /// </summary>
    /// <param name="sessionId">The 'sessionID' route value.</param>
    /// <param name="request">The request body.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtyCreateResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtyCreateResponse> CreatePersistentPtyAsync(string sessionId,
        PersistentPtyCreateRequest request, OpenCodeRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        Raw.CreatePersistentPtyAsync(sessionId, request, requestOptions, cancellationToken);

    /// <summary>
    /// Read the last rows of the session's most recently controlled terminal. The payload is null
    /// when the session has no current terminal.
    /// </summary>
    /// <param name="sessionId">The 'sessionID' route value.</param>
    /// <param name="request">The request shaping the query.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtyReadResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtyReadResponse> ReadAsync(string sessionId,
        PersistentPtyReadRequest? request = null, OpenCodeRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        Raw.GetReadAsync(sessionId, request, requestOptions, cancellationToken);

    /// <summary>
    /// Server-lifecycle operation: prepare a daemon handoff so the terminals outlive this server
    /// until a replacement claims them or the handoff expires. The payload is null when this
    /// server owns no daemon.
    /// </summary>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtyHandoffPostResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtyHandoffPostResponse> HandoffAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PostHandoffAsync(requestOptions, cancellationToken);

    /// <summary>
    /// Server-lifecycle operation: stop the daemon and every terminal it owns. Answers 204 even
    /// when no daemon is running.
    /// </summary>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtyShutdownPostResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtyShutdownPostResponse> ShutdownAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PostShutdownAsync(requestOptions, cancellationToken);

    private PersistentPtysRawClient Raw => _raw ?? throw MockSeam.CreateError("PersistentPtysClient", "RawClient");
}
