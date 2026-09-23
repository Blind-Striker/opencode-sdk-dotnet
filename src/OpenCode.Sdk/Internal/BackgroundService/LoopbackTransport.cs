using System.Net.Sockets;
using OpenCode.Sdk.Internal.Diagnostics;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The discovery probe's own transport: a non-redirecting owned client for one info exchange
/// against the registered endpoint, with two loopback rules the pipeline's transport does not
/// carry. A loopback endpoint is never routed through a proxy, because an environment proxy
/// without <c>NO_PROXY</c> would hide a live daemon. On Windows a loopback connect disables SYN
/// retransmission (<c>SIO_TCP_INITIAL_RTO</c>), because the default connect reports a refused
/// port only after about two seconds — past the pinned probe bound — where Linux, macOS, and the
/// pinned client's own runtime refuse at once; libuv, Go, Bun, curl, and Chromium set the same
/// option for loopback. The probe therefore classifies a dead daemon as no service, never as a
/// timeout, on every host. Separate from <see cref="TransportPolicy"/> so neither rule reaches a
/// public client.
/// </summary>
internal static class LoopbackTransport
{
    /// <summary><c>_WSAIOW(IOC_VENDOR, 17)</c>, the Winsock control code for <c>TCP_INITIAL_RTO_PARAMETERS</c>.</summary>
    private const int SioTcpInitialRto = unchecked((int)0x98000011);

    /// <summary>
    /// <c>TCP_INITIAL_RTO_PARAMETERS { Rtt = TCP_INITIAL_RTO_UNSPECIFIED_RTT (0xFFFF),
    /// MaxSynRetransmissions = TCP_INITIAL_RTO_NO_SYN_RETRANSMISSIONS (0xFE) }</c>, little-endian
    /// with its trailing padding byte; Windows 10 1709 and later honour the no-retransmission value.
    /// </summary>
    private static readonly byte[] NoSynRetransmissions = [0xFF, 0xFF, 0xFE, 0x00];

    /// <summary>Builds the probe's owned client; the pipeline's timeouts never apply, the probe's bound does.</summary>
    public static HttpClient CreateProbeClient(Uri endpoint)
    {
        HttpMessageHandler? handler = null;
        try
        {
            handler = CreateProbeHandler(endpoint);
            var client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            handler = null;
            return client;
        }
        finally
        {
            handler?.Dispose();
        }
    }

    /// <summary>Creates the probe's handler; internal so tests can observe its sealed policy.</summary>
    internal static HttpMessageHandler CreateProbeHandler(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
#if NET
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = !endpoint.IsLoopback,
        };
        if (endpoint.IsLoopback && OperatingSystem.IsWindows())
        {
            handler.ConnectCallback = ConnectWithoutSynRetransmissionAsync;
        }

        return handler;
#else
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = !endpoint.IsLoopback,
        };
#endif
    }

#if NET
    /// <summary>The default connect of <see cref="SocketsHttpHandler"/>, with the loopback option applied first.</summary>
    private static async ValueTask<Stream> ConnectWithoutSynRetransmissionAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        Socket? socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            DisableSynRetransmission(socket);
            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
            var stream = new NetworkStream(socket, ownsSocket: true);
            socket = null;
            return stream;
        }
        finally
        {
            socket?.Dispose();
        }
    }
#else
    /// <summary>
    /// The downlevel answer to a refused loopback port: <see cref="HttpClientHandler"/> has no connect
    /// seam, so the probe learns a refusal first over a raw socket that disables SYN retransmission,
    /// and sends the request only when the port listens. A live daemon costs one extra loopback
    /// handshake; a dead one is reported at once.
    /// </summary>
    /// <returns>True when the endpoint accepted a connection; false when it refused one.</returns>
    public static async Task<bool> IsListeningAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        DisableSynRetransmission(socket);
        try
        {
            await socket.ConnectAsync(new System.Net.DnsEndPoint(endpoint.IdnHost, endpoint.Port), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
#endif

    /// <summary>
    /// Applies the no-retransmission parameters on Windows; where the transport provider refuses
    /// them (Windows before 10 1709) the default connect and its delay remain.
    /// The vendor control code throws on Unix, so the platform check is the guard, not the catch.
    /// </summary>
    [SlopwatchSuppress(
        "SW003",
        "The option is best effort wherever it is set (libuv, Go, the pinned client's runtime): a Windows build that refuses it keeps the default retransmissions, and the connect still runs under the probe's bound.")]
    private static void DisableSynRetransmission(Socket socket)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = socket.IOControl(SioTcpInitialRto, NoSynRetransmissions, optionOutValue: null);
        }
        catch (SocketException)
        {
            // Best effort: the default retransmissions remain, bounded by the probe's timeout.
        }
    }
}
