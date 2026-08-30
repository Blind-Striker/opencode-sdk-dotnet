using System.Net;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

[NotInParallel]
public sealed class OwnedTransportTests
{
#if NET
    [Test]
    public async Task CreateOwnedHttpHandler_Should_Expose_The_Modern_Owned_Policy()
    {
        using var handler = TransportPolicy.CreateOwnedHttpHandler(new Uri("http://localhost"));

        await Assert.That(handler).IsTypeOf<SocketsHttpHandler>();
        var socketsHandler = (SocketsHttpHandler)handler;
        await Assert.That(socketsHandler.AllowAutoRedirect).IsFalse();
        await Assert.That(socketsHandler.PooledConnectionLifetime).IsEqualTo(TimeSpan.FromSeconds(120));
    }
#endif

    /// <summary>
    /// The pinned document declares a required body on a DELETE, and .NET Framework's handler is
    /// the one that refuses a body on the wrong verb, so the body is asserted off the socket on
    /// every target rather than off a stub the platform never sees.
    /// </summary>
    [Test]
    public async Task ExecuteAsync_Should_Send_A_Delete_Body_Through_The_Real_Handler()
    {
        await using var server = LoopbackHttpServer.Start(static _ => new LoopbackHttpResponse
        {
            StatusCode = HttpStatusCode.NoContent,
        });
        using var client = new OpenCodeClient(new OpenCodeClientOptions { Endpoint = server.Endpoint });

        var response = await client.Worktrees.GetProjectWorktreesClient("prj_1").RemoveWorktreeAsync(new WorktreeRemoveRequest
        {
            Directory = "/repo/feature",
            Force = true,
        });

        await Assert.That(response.Status).IsEqualTo(204);
        var request = server.Requests.Single();
        await Assert.That(request.Method).IsEqualTo("DELETE");
        await Assert.That(request.Path).IsEqualTo("/api/worktree/prj_1");
        await Assert.That(request.Body).IsEqualTo("{\"directory\":\"/repo/feature\",\"force\":true}");
    }

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

    [Test]
    public async Task ExecuteAsync_Should_Fail_A_Stalled_Real_Handler_Body_At_The_Progress_Window()
    {
        await using var server = LoopbackHttpServer.Start(static _ => new LoopbackHttpResponse
        {
            StatusCode = HttpStatusCode.OK,
            ContentType = "application/json",
            Body = WireBodyData.HealthOk,
            KeepOpen = true,
        });
        using var handler = TransportPolicy.CreateOwnedHttpHandler(server.Endpoint);
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(
            httpClient, endpoint: server.Endpoint, networkTimeout: TimeSpan.FromMilliseconds(250));

        // The client's own timeout never guards a post-headers read; the progress window is
        // the only timer over a real stalled socket, interrupting through the read token on
        // modern targets and through content disposal on net472.
        _ = await Assert
            .That(async () => _ = await pipeline.ExecuteAsync(
                HttpMethod.Get,
                "/api/health",
                new RecordingResponseAdapter(),
                options: null,
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<OpenCodeTransportException>();
#if NET472
        Task[] disconnectTasks = [server.ClientDisconnected];
        await Task.WhenAll(disconnectTasks).WaitAsync(TimeSpan.FromSeconds(1));
#endif
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

    [Test]
    public async Task ExecuteStreamAsync_Should_Cancel_An_Idle_Real_Handler_Read_On_Net472()
    {
        await using var server = LoopbackHttpServer.Start(static _ => new LoopbackHttpResponse
        {
            StatusCode = HttpStatusCode.OK,
            ContentType = "text/event-stream",
            Body = WireBodyData.Frames(WireBodyData.StreamTestBodyOpen),
            KeepOpen = true,
        });
        using var pipeline = Pipeline.Create(new OpenCodeClientOptions { Endpoint = server.Endpoint });
        using var cancellation = new CancellationTokenSource();
        var enumerator = pipeline.ExecuteStreamAsync(
                HttpMethod.Get, "/api/event", new TestStreamAdapter(), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        Task<bool>? pendingRead = null;
        try
        {
            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            pendingRead = enumerator.MoveNextAsync().AsTask();
            await cancellation.CancelAsync();
            Task[] pendingReads = [pendingRead];

            _ = await Assert
                .That(async () => await Task.WhenAll(pendingReads).WaitAsync(TimeSpan.FromSeconds(1)))
                .Throws<OperationCanceledException>();

            await Assert.That(cancellation.IsCancellationRequested).IsTrue();
            Task[] disconnectTasks = [server.ClientDisconnected];
            await Task.WhenAll(disconnectTasks).WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            server.ReleaseResponses();
            await SettleAfterReleaseAsync(pendingRead);
            await enumerator.DisposeAsync();
        }
    }

    private static async Task SettleAfterReleaseAsync(Task? pendingRead)
    {
        if (pendingRead is null)
        {
            return;
        }

        try
        {
            Task[] pendingReads = [pendingRead];
            await Task.WhenAll(pendingReads).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or OpenCodeTransportException or TimeoutException)
        {
            _ = exception;
        }
    }
#endif
}
