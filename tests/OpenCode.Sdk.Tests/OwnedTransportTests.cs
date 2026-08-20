using System.Net;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

[NotInParallel]
public sealed class OwnedTransportTests
{
#if NET
    [Test]
    public async Task CreateOwnedHttpHandler_Should_Expose_The_Modern_Owned_Policy()
    {
        using var handler = Pipeline.CreateOwnedHttpHandler(new Uri("http://localhost"));

        await Assert.That(handler).IsTypeOf<SocketsHttpHandler>();
        var socketsHandler = (SocketsHttpHandler)handler;
        await Assert.That(socketsHandler.AllowAutoRedirect).IsFalse();
        await Assert.That(socketsHandler.PooledConnectionLifetime).IsEqualTo(TimeSpan.FromSeconds(120));
    }
#endif

    [Test]
    public async Task ExecuteAsync_Should_Surface_A_Redirect_Through_The_Real_Handler()
    {
        await using var server = LoopbackHttpServer.Start(path => path switch
        {
            "/api/health" => new LoopbackHttpResponse
            {
                StatusCode = HttpStatusCode.Found,
                Location = "/redirect-target",
            },
            "/redirect-target" => new LoopbackHttpResponse
            {
                StatusCode = HttpStatusCode.OK,
                ContentType = "application/json",
                Body = WireBodyData.HealthOk,
            },
            _ => new LoopbackHttpResponse { StatusCode = HttpStatusCode.InternalServerError },
        });
        using var client = new OpenCodeClient(new OpenCodeClientOptions { Endpoint = server.Endpoint });

        var exception = await Assert
            .That(async () => _ = await client.GetHealthAsync())
            .Throws<OpenCodeTransportException>();

        await Assert.That(exception!.Message).Contains("302");
        await Assert.That(server.RequestPaths).IsEquivalentTo(["/api/health"]);
    }

#if NET472
    [Test]
    public async Task Create_Should_Configure_The_Endpoint_ServicePoint_For_A_Long_Lived_Client()
    {
        await using var server = LoopbackHttpServer.Start(static _ => new LoopbackHttpResponse
        {
            StatusCode = HttpStatusCode.OK,
        });

        var defaultConnectionLimit = ServicePointManager.DefaultConnectionLimit;
        using var pipeline = Pipeline.Create(new OpenCodeClientOptions { Endpoint = server.Endpoint });
        var servicePoint = ServicePointManager.FindServicePoint(server.Endpoint, WebRequest.DefaultWebProxy);

        await Assert.That(servicePoint.ConnectionLimit).IsEqualTo(int.MaxValue);
        await Assert.That(servicePoint.ConnectionLeaseTimeout).IsEqualTo(120_000);
        await Assert.That(ServicePointManager.DefaultConnectionLimit).IsEqualTo(defaultConnectionLimit);
    }

    [Test]
    public async Task Create_Should_Configure_The_Proxy_ServicePoint_Used_By_The_Owned_Handler()
    {
        await using var proxyServer = LoopbackHttpServer.Start(static _ => new LoopbackHttpResponse
        {
            StatusCode = HttpStatusCode.OK,
        });
        var endpoint = new Uri("http://opencode.invalid");
        var previousProxy = WebRequest.DefaultWebProxy;
        var proxy = new WebProxy(proxyServer.Endpoint, false);
        try
        {
            WebRequest.DefaultWebProxy = proxy;
            using var pipeline = Pipeline.Create(new OpenCodeClientOptions { Endpoint = endpoint });

            var response = await pipeline.ExecuteAsync(
                HttpMethod.Get,
                "/api/health",
                new RecordingResponseAdapter(),
                options: null,
                CancellationToken.None);
            var servicePoint = ServicePointManager.FindServicePoint(endpoint, proxy);

            await Assert.That(response.Status).IsEqualTo(200);
            await Assert.That(servicePoint.ConnectionLimit).IsEqualTo(int.MaxValue);
            await Assert.That(servicePoint.ConnectionLeaseTimeout).IsEqualTo(120_000);
        }
        finally
        {
            WebRequest.DefaultWebProxy = previousProxy;
        }
    }

    [Test]
    public async Task ExecuteAsync_Should_Not_Starve_Behind_Two_Same_Authority_Streams_On_Net472()
    {
        await using var server = LoopbackHttpServer.Start(path => path switch
        {
            "/api/event" => new LoopbackHttpResponse
            {
                StatusCode = HttpStatusCode.OK,
                ContentType = "text/event-stream",
                Body = WireBodyData.Frames(WireBodyData.StreamTestBodyOpen),
                KeepOpen = true,
            },
            "/api/health" => new LoopbackHttpResponse
            {
                StatusCode = HttpStatusCode.OK,
                ContentType = "application/json",
            },
            _ => new LoopbackHttpResponse { StatusCode = HttpStatusCode.InternalServerError },
        });
        using var pipeline = Pipeline.Create(new OpenCodeClientOptions { Endpoint = server.Endpoint });
        await using var first = pipeline.ExecuteStreamAsync(
            HttpMethod.Get, "/api/event", new TestStreamAdapter(), CancellationToken.None).GetAsyncEnumerator();
        await using var second = pipeline.ExecuteStreamAsync(
            HttpMethod.Get, "/api/event", new TestStreamAdapter(), CancellationToken.None).GetAsyncEnumerator();
        var firstOpened = await first.MoveNextAsync();
        var secondOpened = await second.MoveNextAsync();

        var response = await pipeline.ExecuteAsync(
                HttpMethod.Get,
                "/api/health",
                new RecordingResponseAdapter(),
                options: null,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(firstOpened).IsTrue();
        await Assert.That(secondOpened).IsTrue();
        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(server.RequestPaths.Count(static path => path == "/api/event")).IsEqualTo(2);
        await Assert.That(server.RequestPaths.Count(static path => path == "/api/health")).IsEqualTo(1);
    }
#endif
}
