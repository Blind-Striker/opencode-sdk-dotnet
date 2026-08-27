using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk;

/// <summary>
/// A bound 'PtyClient' handle; it holds an immutable identifier and the shared pipeline. The
/// handle's public doors are hand-written over the generated <see cref="PtyRawClient"/>
/// (ADR-0021) because the connect-token handshake needs a value the pinned document does not
/// carry; every represented response still rides the generic envelope machinery.
/// </summary>
public class PtyClient
{
    /// <summary>
    /// Knowledge source: upstream-observed — the server's connect-token handler requires this
    /// exact value; it exists only in upstream implementation source (ADR-0013/0021), so it
    /// lives here in hand-written runtime code and never in curation or generated output.
    /// </summary>
    private const string PtyTicketSentinel = "1";

    private readonly PtyRawClient? _raw;

    internal PtyClient(PtyRawClient raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        _raw = raw;
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
        Raw.PostConnectTokenAsync(request, xOpencodeTicket: PtyTicketSentinel, requestOptions, cancellationToken);

    private PtyRawClient Raw => _raw ?? throw MockSeam.CreateError("PtyClient", "Pipeline");
}
