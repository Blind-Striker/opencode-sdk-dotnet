using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk;

/// <summary>
/// A bound 'PtyClient' handle; it holds a <see cref="PtyRawClient"/> and the
/// <see cref="ConnectionSnapshot"/> the WebSocket door needs. The handle's public doors are
/// hand-written over the generated <see cref="PtyRawClient"/> (ADR-0021) because the
/// connect-token handshake needs a value the pinned document does not carry; every represented
/// response still rides the generic envelope machinery.
/// </summary>
public class PtyClient
{
    private readonly ConnectionSnapshot? _connection;
    private readonly string? _ptyId;
    private readonly PtyRawClient? _raw;

    internal PtyClient(PtyRawClient raw, ConnectionSnapshot connection, string ptyId)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(ptyId);

        _raw = raw;
        _connection = connection;
        _ptyId = ptyId;
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PtyClient()
    {
    }

    /// <summary>
    /// Get PTY session. Get one PTY session, including its exit code once exited.
    /// </summary>
    /// <param name="request">The request shaping the query.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PtyResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 404) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PtyResponse> GetPtyAsync(PtyRequest? request = null,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.GetPtyAsync(request, requestOptions, cancellationToken);

    /// <summary>
    /// Update PTY session. Update the title or viewport size of one PTY session.
    /// </summary>
    /// <param name="request">The request body; an empty body is sent when omitted.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PtyUpdatePutResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 404) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PtyUpdatePutResponse> PutUpdateAsync(PtyUpdatePutRequest? request = null,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PutUpdateAsync(request, requestOptions, cancellationToken);

    /// <summary>
    /// Remove PTY session. Terminate and remove one PTY session.
    /// </summary>
    /// <param name="request">The request shaping the query.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PtyRemoveResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 404) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PtyRemoveResponse> RemovePtyAsync(PtyRemoveRequest? request = null,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.RemovePtyAsync(request, requestOptions, cancellationToken);

    /// <summary>
    /// Create PTY WebSocket token. Create a short-lived single-use ticket for opening a PTY
    /// WebSocket connection. The ticket header the handler requires is applied internally and
    /// is never a caller's argument; the request's location query fixes the scope the ticket
    /// is minted for.
    /// </summary>
    /// <param name="request">The request shaping the query.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PtyConnectTokenPostResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401, 403, 404) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PtyConnectTokenPostResponse> CreateConnectTokenAsync(PtyConnectTokenPostRequest? request = null,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PostConnectTokenAsync(request, xOpencodeTicket: PtyTicketHeader.Sentinel, requestOptions, cancellationToken);

    /// <summary>
    /// Opens the PTY's live WebSocket session. The upgrade is the SDK's one transport
    /// divergence: it builds its own socket, so a caller-supplied <see cref="HttpClient"/>, its
    /// proxy, and its handler chain do not apply. The client's Basic credential rides the upgrade
    /// request's <c>Authorization</c> header — the designed non-browser path — and the SDK never
    /// mints a ticket for its own connection; <see cref="CreateConnectTokenAsync"/> stays the door
    /// for handing a browser one. A missing PTY is refused before the upgrade, while an existing
    /// but already-exited PTY upgrades and then closes, so that failure surfaces on the first read.
    /// </summary>
    /// <param name="options">The connect options: the replay cursor and the per-call location.</param>
    /// <param name="cancellationToken">The cancellation token bounding the upgrade.</param>
    /// <returns>The live session; the caller owns its disposal.</returns>
    /// <exception cref="OpenCodeTransportException">
    /// The upgrade was refused or never completed. A platform that cannot construct the
    /// underlying <see cref="System.Net.WebSockets.ClientWebSocket"/> (pre-Windows-8) maps here
    /// too, naming the platform as the cause, rather than escaping as a raw
    /// <see cref="PlatformNotSupportedException"/>.
    /// </exception>
    public virtual async Task<PtySession> ConnectAsync(PtyConnectOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var connection = Connection;
        var ptyId = PtyId;
        var address = PtyConnectUriBuilder.Build(connection, ptyId, options);
        ClientPtyWebSocket socket;
        try
        {
            socket = new ClientPtyWebSocket(connection.Authorization);
        }
        catch (PlatformNotSupportedException exception)
        {
            throw new OpenCodeTransportException(
                $"The opencode PTY '{ptyId}' WebSocket could not be constructed on this platform.", exception);
        }

        try
        {
            await socket.ConnectAsync(address, ptyId, cancellationToken).ConfigureAwait(false);
            return new PtySession(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private ConnectionSnapshot Connection => _connection ?? throw MockSeam.CreateError("PtyClient", "Snapshot");

    private string PtyId => _ptyId ?? throw MockSeam.CreateError("PtyClient", "PtyId");

    private PtyRawClient Raw => _raw ?? throw MockSeam.CreateError("PtyClient", "RawClient");
}
