using System.Globalization;
using System.Net.WebSockets;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Names why a PTY WebSocket upgrade never completed. The server answers a missing PTY with a
/// plain HTTP 404 and a refused credential or origin with 401/403 <em>before</em> upgrading, so
/// there is no response spine for the failure to ride and no envelope for ADR-0007's machinery to
/// materialize: the transport plane is the honest channel. On targets whose
/// <see cref="ClientWebSocket"/> cannot report the response status the failure still names the
/// connect context it does know.
/// </summary>
internal static class PtyUpgradeFailurePolicy
{
    /// <summary>Maps a failed upgrade onto the transport failure that explains it.</summary>
    /// <param name="exception">The failure <see cref="ClientWebSocket.ConnectAsync(Uri, CancellationToken)"/> raised.</param>
    /// <param name="status">The HTTP status the server answered, or null when the target cannot report one.</param>
    /// <param name="ptyId">The PTY the upgrade addressed.</param>
    /// <returns>The transport failure to throw.</returns>
    public static OpenCodeTransportException Map(WebSocketException exception, int? status, string ptyId)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (status is null)
        {
            return new OpenCodeTransportException(
                $"The opencode PTY '{ptyId}' WebSocket upgrade failed before the connection was established.",
                exception);
        }

        var code = status.Value.ToString(CultureInfo.InvariantCulture);
        return status switch
        {
            404 => new OpenCodeTransportException(
                $"The opencode server answered the PTY '{ptyId}' WebSocket upgrade with HTTP {code}; the PTY session does not exist.",
                exception),
            401 or 403 => new OpenCodeTransportException(
                $"The opencode server refused the PTY '{ptyId}' WebSocket upgrade with HTTP {code}; the request's credential was rejected.",
                exception),
            _ => new OpenCodeTransportException(
                $"The opencode server answered the PTY '{ptyId}' WebSocket upgrade with HTTP {code} instead of completing the protocol upgrade.",
                exception),
        };
    }
}
