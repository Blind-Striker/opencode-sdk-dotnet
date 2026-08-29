using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk;

/// <summary>
/// A bound 'PersistentPtyClient' handle over a <see cref="PersistentPtyRawClient"/>. The handle's
/// public doors are hand-written (ADR-0021) because the connect-token handshake needs a value the
/// pinned document does not carry; every represented response still rides the generic envelope
/// machinery, so route, status, and schema drift breaks compilation through the raw twin.
/// </summary>
public class PersistentPtyClient
{
    private readonly PersistentPtyRawClient? _raw;

    internal PersistentPtyClient(PersistentPtyRawClient raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        _raw = raw;
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PersistentPtyClient()
    {
    }

    /// <summary>
    /// Get one persistent terminal. 404 also answers when the daemon is not running.
    /// </summary>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtyResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 404, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtyResponse> GetPersistentPtyAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.GetPersistentPtyAsync(requestOptions, cancellationToken);

    /// <summary>
    /// Resize one persistent terminal; the resize also selects it as the session's current terminal.
    /// </summary>
    /// <param name="request">The request body.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtyUpdatePutResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 404, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtyUpdatePutResponse> UpdatePersistentPtyAsync(PersistentPtyUpdatePutRequest request,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PutUpdateAsync(request, requestOptions, cancellationToken);

    /// <summary>
    /// Terminate and remove one persistent terminal.
    /// </summary>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtyRemoveResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 404, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtyRemoveResponse> RemovePersistentPtyAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.RemovePersistentPtyAsync(requestOptions, cancellationToken);

    /// <summary>
    /// Snapshot one persistent terminal: its info, the retained text, the screen checkpoint as
    /// the terminal-escape bytes an emulator sized to <c>Info.Size</c> replays, and the cursor.
    /// </summary>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtySnapshotResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 404, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtySnapshotResponse> GetSnapshotAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.GetSnapshotAsync(requestOptions, cancellationToken);

    /// <summary>
    /// Create a short-lived single-use ticket for a browser's WebSocket upgrade. The ticket header
    /// the handler requires is applied internally and is never a caller's argument; the ticket is
    /// scoped to this terminal only.
    /// </summary>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PersistentPtyConnectTokenPostResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 403, 404, 503) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PersistentPtyConnectTokenPostResponse> CreateConnectTokenAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PostConnectTokenAsync(xOpencodeTicket: PtyTicketHeader.Sentinel, requestOptions, cancellationToken);

    private PersistentPtyRawClient Raw => _raw ?? throw MockSeam.CreateError("PersistentPtyClient", "RawClient");
}
