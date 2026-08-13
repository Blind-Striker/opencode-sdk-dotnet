using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using NSubstitute;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Internal.Abstractions;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class PipelineTests
{
    private const string PasswordVariable = "OPENCODE_SERVER_PASSWORD";
    private static readonly Uri Endpoint = new("http://localhost:4096");

    [Test]
    public async Task ExecuteAsync_Should_Join_The_Route_Onto_The_Endpoint()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None);

        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/health"));
    }

    [Test]
    public async Task ExecuteAsync_Should_Preserve_The_Endpoint_Path_Prefix()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient, endpoint: new Uri("http://localhost:8080/opencode/"));

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None);

        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:8080/opencode/api/health"));
    }

    [Test]
    public async Task ExecuteAsync_Should_Decorate_Basic_Auth_From_The_Explicit_Password()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        var environment = Substitute.For<IEnvironmentProvider>();
        using var pipeline = CreatePipeline(httpClient, password: "secret", environment: environment);

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None);

        var expected = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:secret"))}";
        await Assert.That(handler.Requests.Single().Authorization).IsEqualTo(expected);
        _ = environment.DidNotReceive().GetEnvironmentVariable(Arg.Any<string>());
    }

    [Test]
    public async Task ExecuteAsync_Should_Resolve_The_Environment_Password_Once_At_Construction()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        var environment = Substitute.For<IEnvironmentProvider>();
        _ = environment.GetEnvironmentVariable(PasswordVariable).Returns("fallback");
        using var pipeline = CreatePipeline(httpClient, environment: environment);

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None);
        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None);

        var expected = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:fallback"))}";
        await Assert.That(handler.Requests.All(request => request.Authorization == expected)).IsTrue();
        _ = environment.Received(1).GetEnvironmentVariable(PasswordVariable);
    }

    [Test]
    public async Task ExecuteAsync_Should_Send_Anonymously_When_No_Password_Resolves()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None);

        await Assert.That(handler.Requests.Single().Authorization).IsNull();
    }

    [Test]
    public async Task ExecuteAsync_Should_Decorate_The_User_Agent_Per_Request()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None);

        await Assert.That(handler.Requests.Single().UserAgent).IsEqualTo(UserAgentPolicy.Resolve().ToString());
    }

    [Test]
    public async Task ExecuteAsync_Should_Never_Touch_The_Client_Defaults()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient, password: "secret");

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None);

        await Assert.That(httpClient.BaseAddress).IsNull();
        await Assert.That(httpClient.DefaultRequestHeaders.Any()).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_Should_Buffer_The_Body_For_The_Adapter()
    {
        using var handler = new RecordingHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"healthy\":true}"),
        });
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);
        var adapter = new RecordingResponseAdapter();

        var response = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", adapter, options: null, CancellationToken.None);

        await Assert.That(adapter.AdaptedStatus).IsEqualTo(200);
        await Assert.That(adapter.AdaptedRawBody).IsEqualTo("{\"healthy\":true}");
        await Assert.That(response.Status).IsEqualTo(200);
    }

    [Test]
    public async Task ExecuteAsync_Should_Throw_The_Api_Exception_By_Default()
    {
        using var handler = new RecordingHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"_tag\":\"UnauthorizedError\"}"),
        });
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);
        var error = new UnauthorizedError
        {
            Message = "password required",
        };
        var adapter = new RecordingResponseAdapter((status, rawBody) => new TestResponse
        {
            Status = status,
            IsError = true,
            Error = error,
            RawBody = rawBody,
        });

        var exception = await Assert
            .That(async () => _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", adapter, options: null, CancellationToken.None))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(401);
        await Assert.That(exception.Error).IsSameReferenceAs(error);
        await Assert.That(exception.RawBody).IsEqualTo("{\"_tag\":\"UnauthorizedError\"}");
        await Assert.That(exception.Message).Contains("401");
        await Assert.That(exception.Message).Contains("UnauthorizedError");
    }

    [Test]
    public async Task ExecuteAsync_Should_Return_The_Error_Envelope_When_NoThrow()
    {
        using var handler = new RecordingHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"_tag\":\"UnauthorizedError\"}"),
        });
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);
        var adapter = new RecordingResponseAdapter(static (status, rawBody) => new TestResponse
        {
            Status = status,
            IsError = true,
            RawBody = rawBody,
        });

        var response = await pipeline.ExecuteAsync(
            HttpMethod.Get,
            "/api/health",
            adapter,
            OpenCodeRequestOptions.NoThrow,
            CancellationToken.None);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.RawBody).IsEqualTo("{\"_tag\":\"UnauthorizedError\"}");
    }

    [Test]
    public async Task ExecuteAsync_Should_Wrap_Network_Failures_As_Transport_Failures()
    {
        using var handler = new RecordingHttpHandler(static _ => throw new HttpRequestException("connection refused"));
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);

        var exception = await Assert
            .That(async () => _ = await pipeline.ExecuteAsync(
                HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None))
            .Throws<OpenCodeTransportException>();

        await Assert.That(exception!.InnerException).IsTypeOf<HttpRequestException>();
    }

    [Test]
    public async Task ExecuteAsync_Should_Wrap_An_Invalid_Charset_As_A_Transport_Failure()
    {
        using var handler = new RecordingHttpHandler(static _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("{\"healthy\":true}"u8.ToArray()),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "not-an-encoding" };
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);

        var exception = await Assert
            .That(async () => _ = await pipeline.ExecuteAsync(
                HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None))
            .Throws<OpenCodeTransportException>();

        await Assert.That(exception!.InnerException).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task ExecuteAsync_Should_Wrap_A_Disposal_Race_On_Send_As_A_Transport_Failure()
    {
        using var handler = new RecordingHttpHandler(static _ => throw new ObjectDisposedException(nameof(HttpClient)));
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);

        var exception = await Assert
            .That(async () => _ = await pipeline.ExecuteAsync(
                HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None))
            .Throws<OpenCodeTransportException>();

        await Assert.That(exception!.InnerException).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task ExecuteAsync_Should_Refuse_An_Undefined_Error_Behavior_Before_Sending()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);
        var options = new OpenCodeRequestOptions { ErrorBehavior = (ErrorBehavior)2 };

        _ = await Assert
            .That(async () => _ = await pipeline.ExecuteAsync(
                HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options, CancellationToken.None))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(handler.Requests.Any()).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_Should_Pass_Cancellation_Through()
    {
        using var handler = new RecordingHttpHandler(static _ => throw new OperationCanceledException());
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);

        _ = await Assert
            .That(async () => _ = await pipeline.ExecuteAsync(
                HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Dispose_Should_Dispose_The_Owned_Client()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        var pipeline = CreatePipeline(httpClient, ownsHttpClient: true);

        pipeline.Dispose();

        await Assert.That(handler.IsDisposed).IsTrue();
    }

    [Test]
    public async Task Dispose_Should_Never_Dispose_An_Injected_Client()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        var pipeline = CreatePipeline(httpClient);

        pipeline.Dispose();

        await Assert.That(handler.IsDisposed).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_Should_Refuse_After_Dispose()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        var pipeline = CreatePipeline(httpClient);
        pipeline.Dispose();

        _ = await Assert
            .That(async () => _ = await pipeline.ExecuteAsync(
                HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ExecuteAsync_Should_Refuse_A_Route_Without_A_Leading_Slash()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = CreatePipeline(httpClient);

        _ = await Assert
            .That(async () => _ = await pipeline.ExecuteAsync(
                HttpMethod.Get, "api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Create_Should_Refuse_An_Injected_Client_Without_An_Endpoint()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);

        var exception = Assert.Throws<ArgumentException>(() => _ = Pipeline.Create(
            httpClient,
            new OpenCodeClientOptions(),
            Substitute.For<IEnvironmentProvider>()));

        await Assert.That(exception.Message).Contains("Endpoint");
    }

    [Test]
    public async Task Create_Should_Refuse_A_Conflicting_Options_Endpoint()
    {
        var options = new OpenCodeClientOptions
        {
            Endpoint = new Uri("http://other:1"),
        };

        var exception = Assert.Throws<ArgumentException>(() => _ = Pipeline.Create(
            Endpoint,
            options,
            Substitute.For<IEnvironmentProvider>()));

        await Assert.That(exception.Message).Contains("endpoint");
    }

    [Test]
    public async Task Create_Should_Build_An_Owned_Pipeline_From_The_Endpoint()
    {
        using var pipeline = Pipeline.Create(Endpoint, options: null, Substitute.For<IEnvironmentProvider>());

        await Assert.That(pipeline).IsNotNull();
    }

    [Test]
    public async Task UserAgent_Should_Match_The_Assembly_Informational_Version()
    {
        var informational = typeof(Pipeline).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var product = UserAgentPolicy.Resolve().ToString();

        await Assert.That(informational).IsNotNull();
        await Assert.That(product).IsEqualTo($"OpenCode.Sdk/{informational!.Split('+')[0]}");
    }

    private static Pipeline CreatePipeline(
        HttpClient httpClient,
        bool ownsHttpClient = false,
        Uri? endpoint = null,
        string? password = null,
        IEnvironmentProvider? environment = null) =>
        new(httpClient, ownsHttpClient, endpoint ?? Endpoint, password, environment ?? Substitute.For<IEnvironmentProvider>());
}
