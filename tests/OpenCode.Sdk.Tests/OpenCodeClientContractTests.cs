using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class OpenCodeClientContractTests
{
    private static readonly Uri Endpoint = new("http://localhost:4096");

    [Test]
    public async Task GetHealthAsync_Should_Return_The_Typed_Payload()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.OK,
            "{\"healthy\":true,\"version\":\"0.0.0-test\",\"pid\":42}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.GetHealthAsync();

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.RawBody).IsNull();
        await Assert.That(response.Health.Healthy).IsTrue();
        await Assert.That(response.Health.Version).IsEqualTo("0.0.0-test");
        await Assert.That(response.Health.Pid).IsEqualTo(42);
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/health"));
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Get);
    }

    [Test]
    public async Task GetHealthAsync_Should_Throw_The_Typed_Error_By_Default()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.Unauthorized,
            "{\"_tag\":\"UnauthorizedError\",\"message\":\"password required\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.GetHealthAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(401);
        await Assert.That(exception.Error).IsTypeOf<UnauthorizedError>();
        await Assert.That(((UnauthorizedError)exception.Error!).Message).IsEqualTo("password required");
        await Assert.That(exception.RawBody).Contains("UnauthorizedError");
    }

    [Test]
    public async Task GetMessageAsync_Should_Unwrap_The_Data_Envelope()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session-message.json");
        using var handler = new RecordingHttpHandler(_ => JsonResponse(HttpStatusCode.OK, $"{{\"data\":{payload}}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.Sessions.GetSessionClient("ses_1 x").GetMessageAsync("msg_2/y");

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.Message).IsTypeOf<SessionMessageUser>();
        await Assert.That(((SessionMessageUser)response.Message).Id).IsEqualTo("message-1");
        await Assert.That(handler.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/ses_1%20x/message/msg_2%2Fy");
    }

    [Test]
    public async Task GetMessageAsync_Should_Type_Both_Declared_404_Errors()
    {
        using var sessionHandler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.NotFound,
            "{\"_tag\":\"SessionNotFoundError\",\"sessionID\":\"s\",\"message\":\"gone\"}"));
        using var sessionHttpClient = new HttpClient(sessionHandler);
        using var sessionClient = CreateClient(sessionHttpClient);
        using var messageHandler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.NotFound,
            "{\"_tag\":\"MessageNotFoundError\",\"sessionID\":\"s\",\"messageID\":\"m\",\"message\":\"gone\"}"));
        using var messageHttpClient = new HttpClient(messageHandler);
        using var messageClient = CreateClient(messageHttpClient);

        var sessionMiss = await Assert
            .That(async () => _ = await sessionClient.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();
        var messageMiss = await Assert
            .That(async () => _ = await messageClient.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();

        await Assert.That(sessionMiss!.Error).IsTypeOf<SessionNotFoundError>();
        await Assert.That(messageMiss!.Error).IsTypeOf<MessageNotFoundError>();
    }

    [Test]
    public async Task GetMessageAsync_Should_Return_The_Error_Envelope_When_NoThrow()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.NotFound,
            "{\"_tag\":\"SessionNotFoundError\",\"sessionID\":\"s\",\"message\":\"gone\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.Sessions.GetSessionClient("s").GetMessageAsync("m", OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(404);
        await Assert.That(response.Error).IsTypeOf<SessionNotFoundError>();
        await Assert.That(response.RawBody).Contains("SessionNotFoundError");
    }

    [Test]
    public async Task GetMessageAsync_Should_Downgrade_An_Unknown_Tag_To_The_Carrier()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.NotFound,
            "{\"_tag\":\"BrandNewError\",\"detail\":7}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();

        var unknown = (UnknownOpenCodeError)exception!.Error!;
        await Assert.That(unknown.Tag).IsEqualTo("BrandNewError");
        await Assert.That(unknown.Payload.GetProperty("detail").GetInt32()).IsEqualTo(7);
    }

    [Test]
    public async Task GetMessageAsync_Should_Downgrade_An_Undeclared_Status_To_The_Carrier()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            (HttpStatusCode)418,
            "{\"_tag\":\"UnauthorizedError\",\"message\":\"weird\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(418);
        await Assert.That(exception.Error).IsTypeOf<UnknownOpenCodeError>();
        await Assert.That(((UnknownOpenCodeError)exception.Error!).Tag).IsEqualTo("UnauthorizedError");
    }

    [Test]
    public async Task GetMessageAsync_Should_Downgrade_A_Known_Tag_At_The_Wrong_Status()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.BadRequest,
            "{\"_tag\":\"SessionNotFoundError\",\"sessionID\":\"s\",\"message\":\"misplaced\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<UnknownOpenCodeError>();
        await Assert.That(((UnknownOpenCodeError)exception.Error!).Tag).IsEqualTo("SessionNotFoundError");
    }

    [Test]
    public async Task GetHealthAsync_Should_Preserve_The_Raw_Body_For_Malformed_Errors()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.BadRequest,
            "<html>not json</html>"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var thrown = await Assert
            .That(async () => _ = await client.GetHealthAsync())
            .Throws<OpenCodeApiException>();
        await Assert.That(thrown!.Error).IsNull();
        await Assert.That(thrown.RawBody).IsEqualTo("<html>not json</html>");

        using var noThrowHandler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.BadRequest,
            "<html>not json</html>"));
        using var noThrowHttpClient = new HttpClient(noThrowHandler);
        using var noThrowClient = CreateClient(noThrowHttpClient);
        var response = await noThrowClient.GetHealthAsync(OpenCodeRequestOptions.NoThrow);
        await Assert.That(response.Error).IsNull();
        await Assert.That(response.RawBody).IsEqualTo("<html>not json</html>");
    }

    [Test]
    public async Task GetHealthAsync_Should_Treat_A_Malformed_Success_As_A_Protocol_Failure()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(HttpStatusCode.OK, "not json"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await Assert
            .That(async () => _ = await client.GetHealthAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetHealthAsync_Should_Treat_An_Undeclared_2xx_As_A_Protocol_Failure()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.Created,
            "{\"healthy\":true,\"version\":\"0.0.0-test\",\"pid\":42}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await Assert
            .That(async () => _ = await client.GetHealthAsync())
            .Throws<OpenCodeTransportException>();

        _ = await Assert
            .That(async () => _ = await client.GetHealthAsync(OpenCodeRequestOptions.NoThrow))
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetHealthAsync_Should_Preserve_The_Raw_Body_For_An_Empty_Error_Tag()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.BadRequest,
            "{\"_tag\":\"\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.GetHealthAsync(OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Error).IsNull();
        await Assert.That(response.RawBody).IsEqualTo("{\"_tag\":\"\"}");
    }

    [Test]
    public async Task GetMessageAsync_Should_Treat_An_Empty_Success_Marker_As_A_Protocol_Failure()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.OK,
            "{\"data\":{\"type\":\"\",\"id\":\"m\"}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetMessageAsync_Should_Escape_Route_Values_Into_The_Request_Uri()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.OK,
            "{\"data\":{\"id\":\"message-1\",\"time\":{\"created\":1},\"text\":\"hello\",\"type\":\"user\"}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await client.Sessions.GetSessionClient("a b").GetMessageAsync("c/d");

        await Assert.That(handler.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/a%20b/message/c%2Fd");
    }

    [Test]
    public async Task Payload_Should_Guard_Access_And_Printing_On_The_Error_Path()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.Unauthorized,
            "{\"_tag\":\"UnauthorizedError\",\"message\":\"no\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.GetHealthAsync(OpenCodeRequestOptions.NoThrow);

        _ = Assert.Throws<InvalidOperationException>(() => _ = response.Health);
        var printed = response.ToString();
        await Assert.That(printed).Contains("401");
        await Assert.That(printed).DoesNotContain("Health = ");
    }

    [Test]
    public async Task Client_Should_Fail_Instructively_On_The_Unoverridden_Mock_Seam()
    {
        using var client = new MockableClient();

        var exception = await Assert
            .That(async () => _ = await client.GetHealthAsync())
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("mocking constructor");
        _ = Assert.Throws<InvalidOperationException>(() => _ = client.Sessions);
    }

    [Test]
    public async Task Dispose_Should_Make_The_Bound_Handles_Unusable()
    {
        var client = new OpenCodeClient(Endpoint);
        var session = client.Sessions.GetSessionClient("s");
        client.Dispose();

        _ = await Assert
            .That(async () => _ = await session.GetMessageAsync("m"))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Constructor_Should_Require_An_Endpoint_For_A_Byo_Client()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);

        _ = Assert.Throws<ArgumentException>(() => _ = new OpenCodeClient(httpClient, new OpenCodeClientOptions()));
    }

    private static OpenCodeClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new OpenCodeClientOptions
        {
            Endpoint = Endpoint,
        });

    private sealed class MockableClient : OpenCodeClient
    {
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body),
        };
}
