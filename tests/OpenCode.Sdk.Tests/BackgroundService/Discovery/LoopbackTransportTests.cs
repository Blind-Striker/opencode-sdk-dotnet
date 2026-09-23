using OpenCode.Sdk.Internal.BackgroundService.Discovery;

namespace OpenCode.Sdk.Tests.BackgroundService.Discovery;

/// <summary>
/// The probe transport's sealed policy, read off the handler it builds: a loopback endpoint is
/// never routed through a proxy (an environment proxy without <c>NO_PROXY</c> would otherwise hide
/// a live daemon), any other endpoint keeps the platform default, and no redirect is followed.
/// </summary>
public sealed class LoopbackTransportTests
{
    [Test]
    [Arguments("http://127.0.0.1:4096/")]
    [Arguments("http://[::1]:4096/")]
    [Arguments("http://localhost:4096/")]
    public async Task CreateProbeHandler_Should_Bypass_Every_Proxy_For_A_Loopback_Endpoint(string endpoint)
    {
        using var handler = LoopbackTransport.CreateProbeHandler(new Uri(endpoint));

        await Assert.That(UseProxy(handler)).IsFalse();
        await Assert.That(AllowAutoRedirect(handler)).IsFalse();
    }

    [Test]
    public async Task CreateProbeHandler_Should_Keep_The_Proxy_Default_For_Another_Endpoint()
    {
        using var handler = LoopbackTransport.CreateProbeHandler(new Uri("http://opencode.example:4096/"));

        await Assert.That(UseProxy(handler)).IsTrue();
        await Assert.That(AllowAutoRedirect(handler)).IsFalse();
    }

    private static bool UseProxy(HttpMessageHandler handler) => handler switch
    {
#if NET
        SocketsHttpHandler sockets => sockets.UseProxy,
#endif
        HttpClientHandler client => client.UseProxy,
        _ => throw new InvalidOperationException("The probe handler is not a platform handler."),
    };

    private static bool AllowAutoRedirect(HttpMessageHandler handler) => handler switch
    {
#if NET
        SocketsHttpHandler sockets => sockets.AllowAutoRedirect,
#endif
        HttpClientHandler client => client.AllowAutoRedirect,
        _ => throw new InvalidOperationException("The probe handler is not a platform handler."),
    };
}
