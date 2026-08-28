using System.Net;
using System.Net.Sockets;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Reserves a currently-free loopback port by binding port zero and releasing it. The window
/// between release and the server's own bind is a named residual risk of the drive manifest
/// contract, which requires explicit ports (manifest.ts:12-24); per-run instances keep the
/// exposure to one bind per suite run.
/// </summary>
internal static class LoopbackPortReservation
{
    public static int Reserve()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
#if NET
            // TcpListener implements IDisposable from .NET 8 on; Stop() alone does not satisfy
            // CA2000 there. Downlevel targets have no Dispose — Stop() is their full release.
            listener.Dispose();
#endif
        }
    }
}
