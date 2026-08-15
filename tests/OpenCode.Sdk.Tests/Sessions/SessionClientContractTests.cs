using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class SessionClientContractTests
{
    private static readonly Uri Endpoint = new("http://localhost:4096");

    [Test]
    public async Task GetSessionAsync_Should_Return_The_Typed_Session()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var handler = new RecordingHttpHandler(_ => JsonResponse(HttpStatusCode.OK, $"{{\"data\":{payload}}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.Sessions.GetSessionClient("ses_100").GetSessionAsync();

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.Session.Id).IsEqualTo("ses_100");
        await Assert.That(response.Session.Location.Directory).IsEqualTo("/repo");
        await Assert.That(response.Session.Tokens.Cache.Read).IsEqualTo(1);
        await Assert.That(handler.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100"));
    }

    [Test]
    public async Task GetSessionAsync_Should_Treat_An_Explicit_Null_Parent_As_A_Malformed_Success()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.null-parent-session.json");
        using var handler = new RecordingHttpHandler(_ => JsonResponse(HttpStatusCode.OK, $"{{\"data\":{payload}}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("ses_100").GetSessionAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetSessionAsync_Should_Throw_The_Declared_404_Error()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.NotFound,
            "{\"_tag\":\"SessionNotFoundError\",\"sessionID\":\"ses_9\",\"message\":\"gone\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("ses_9").GetSessionAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(404);
        await Assert.That(exception.Error).IsTypeOf<SessionNotFoundError>();
    }

    [Test]
    public async Task GetSessionAsync_Should_Throw_The_Declared_401_Error()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.Unauthorized,
            "{\"_tag\":\"UnauthorizedError\",\"message\":\"password required\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("ses_9").GetSessionAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Error).IsTypeOf<UnauthorizedError>();
    }

    [Test]
    public async Task ListMessagesAsync_Should_Return_The_Typed_Page_With_Its_Cursor()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session-message.json");
        using var handler = new RecordingHttpHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $"{{\"data\":[{payload}],\"cursor\":{{\"previous\":\"cur_0\"}}}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.Sessions.GetSessionClient("ses_100").ListMessagesAsync(new MessageListRequest
        {
            Limit = 2,
            Order = ListOrder.Ascending,
        });

        await Assert.That(response.Messages.Single()).IsTypeOf<SessionMessageUser>();
        await Assert.That(response.Cursor.Previous).IsEqualTo("cur_0");
        await Assert.That(response.Cursor.Next).IsNull();
        await Assert.That(handler.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/ses_100/message?limit=2&order=asc");
    }

    [Test]
    public async Task ListMessagesAsync_Should_Treat_A_Null_Page_Element_As_A_Protocol_Failure()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.OK,
            "{\"data\":[null],\"cursor\":{}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("ses_100").ListMessagesAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ListMessagesAsync_Should_Throw_The_Declared_500_Error()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.InternalServerError,
            "{\"_tag\":\"UnknownError\",\"message\":\"boom\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("ses_100").ListMessagesAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(500);
        await Assert.That(exception.Error).IsTypeOf<UnknownError>();
        await Assert.That(((UnknownError)exception.Error!).Message).IsEqualTo("boom");
    }

    [Test]
    public async Task ListMessagesAsync_Should_Return_The_500_Error_On_The_NoThrow_Spine()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.InternalServerError,
            "{\"_tag\":\"UnknownError\",\"message\":\"boom\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.Sessions.GetSessionClient("ses_100")
            .ListMessagesAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(500);
        await Assert.That(response.Error).IsTypeOf<UnknownError>();
        await Assert.That(response.RawBody).Contains("boom");
    }

    [Test]
    public async Task ListMessagesAsync_Should_Throw_The_Declared_404_Error()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.NotFound,
            "{\"_tag\":\"SessionNotFoundError\",\"sessionID\":\"ses_9\",\"message\":\"gone\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.Sessions.GetSessionClient("ses_9").ListMessagesAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Error).IsTypeOf<SessionNotFoundError>();
    }

    private static OpenCodeClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new OpenCodeClientOptions
        {
            Endpoint = Endpoint,
        });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body),
        };
}
