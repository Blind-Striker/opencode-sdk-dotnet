using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk;

/// <summary>
/// The 'PtysClient' collection client. Every public door of the normal PTY family is
/// hand-written over the generated internal raw clients (ADR-0021): the family's center of
/// gravity is a live WebSocket session and a token handshake whose knowledge the pinned
/// document does not carry, so the surface is owned here while route, status, and schema drift
/// still breaks compilation through <see cref="PtysRawClient"/>.
/// </summary>
public class PtysClient
{
    private readonly ConnectionSnapshot? _connection;
    private readonly PtysRawClient? _raw;

    internal PtysClient(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _raw = new PtysRawClient(pipeline);

        // The WebSocket door cannot ride the pipeline's policies, so the family carries the
        // connection facts down to the bound handle that opens the session.
        _connection = pipeline.Connection;
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PtysClient()
    {
    }

    /// <summary>
    /// Gets a bound 'PtyClient'; the handle never caches server state.
    /// </summary>
    /// <param name="ptyId">The 'ptyID' route value.</param>
    /// <returns>The bound 'PtyClient'.</returns>
    public virtual PtyClient GetPtyClient(string ptyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ptyId);
        if (ptyId is "." or "..")
        {
            throw new ArgumentException("Route values must not be dot segments.", nameof(ptyId));
        }

        return new PtyClient(Raw.GetPtyRawClient(ptyId), Connection, ptyId);
    }

    /// <summary>
    /// Create PTY session. Create a pseudo-terminal session for a location.
    /// </summary>
    /// <param name="request">The request body; an empty body is sent when omitted.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PtyCreateResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PtyCreateResponse> CreatePtyAsync(PtyCreateRequest? request = null,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.CreatePtyAsync(request, requestOptions, cancellationToken);

    /// <summary>
    /// List PTY sessions. List PTY sessions for a location, including exited sessions retained until removal.
    /// </summary>
    /// <param name="request">The request shaping the query.</param>
    /// <param name="requestOptions">The per-call options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 'PtyListResponse' envelope.</returns>
    /// <exception cref="OpenCodeApiException">The API returned an error status (declared: 400, 401) and NoThrow was not selected.</exception>
    /// <exception cref="OpenCodeTransportException">The server could not be reached or returned a malformed success body.</exception>
    public virtual Task<PtyListResponse> ListPtysAsync(PtyListRequest? request = null,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.ListPtysAsync(request, requestOptions, cancellationToken);

    private ConnectionSnapshot Connection => _connection ?? throw MockSeam.CreateError("PtysClient", "Snapshot");

    private PtysRawClient Raw => _raw ?? throw MockSeam.CreateError("PtysClient", "RawClient");
}
