using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class SessionsClientContractTests
{
    private static readonly Uri Endpoint = new("http://localhost:4096");

    [Test]
    public async Task ListSessionsAsync_Should_Return_The_Typed_Page_With_Its_Cursor()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var handler = new RecordingHttpHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $"{{\"data\":[{payload}],\"cursor\":{{\"next\":\"cur_2\"}}}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.Sessions.ListSessionsAsync();

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.Sessions.Single().Id).IsEqualTo("ses_100");
        await Assert.That(response.Sessions.Single().Title).IsEqualTo("Fix the build");
        await Assert.That(response.Cursor.Next).IsEqualTo("cur_2");
        await Assert.That(response.Cursor.Previous).IsNull();
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/session"));
    }

    [Test]
    public async Task ListSessionsAsync_Should_Compose_The_Escaped_Query()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.OK,
            "{\"data\":[],\"cursor\":{}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await client.Sessions.ListSessionsAsync(new SessionListOptions
        {
            Limit = 5,
            Order = ListOrder.Descending,
            Search = "a b",
            ParentId = SessionParentFilter.RootOnly,
            Cursor = "cur_1",
        });

        await Assert.That(handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo(
            "http://localhost:4096/api/session?limit=5&order=desc&search=a%20b&parentID=null&cursor=cur_1");
    }

    [Test]
    public async Task ListSessionsAsync_Should_Refuse_A_Non_Positive_Limit_Before_Sending()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await Assert
            .That(async () => _ = await client.Sessions.ListSessionsAsync(new SessionListOptions { Limit = 0 }))
            .Throws<ArgumentException>();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("{\"_tag\":\"InvalidCursorError\",\"message\":\"stale\"}", typeof(InvalidCursorError))]
    [Arguments("{\"_tag\":\"InvalidRequestError\",\"message\":\"bad\"}", typeof(InvalidRequestError))]
    public async Task ListSessionsAsync_Should_Type_Both_Declared_400_Errors(string body, Type expected)
    {
        using var handler = new RecordingHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest, body));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.Sessions.ListSessionsAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error!.GetType()).IsEqualTo(expected);
    }

    [Test]
    public async Task ListSessionsAsync_Should_Treat_An_Undeclared_Success_As_A_Protocol_Failure()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(HttpStatusCode.Accepted, "{}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await Assert
            .That(async () => _ = await client.Sessions.ListSessionsAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ListSessionsAsync_Should_Treat_A_Missing_Envelope_As_A_Protocol_Failure()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await Assert
            .That(async () => _ = await client.Sessions.ListSessionsAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task CreateSessionAsync_Should_Send_The_Empty_Body_When_Omitted()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var handler = new RecordingHttpHandler(_ => JsonResponse(HttpStatusCode.OK, $"{{\"data\":{payload}}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.Sessions.CreateSessionAsync();

        await Assert.That(response.Session.Id).IsEqualTo("ses_100");
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Requests.Single().ContentType).IsEqualTo("application/json");
        await Assert.That(handler.Requests.Single().Body).IsEqualTo("{}");
    }

    [Test]
    public async Task CreateSessionAsync_Should_Send_The_Typed_Body()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var handler = new RecordingHttpHandler(_ => JsonResponse(HttpStatusCode.OK, $"{{\"data\":{payload}}}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        _ = await client.Sessions.CreateSessionAsync(new SessionCreateRequest
        {
            Title = "Fresh session",
        });

        await Assert.That(handler.Requests.Single().Body).IsEqualTo("{\"title\":\"Fresh session\"}");
    }

    [Test]
    public async Task CreateSessionAsync_Should_Throw_The_Declared_400_Error()
    {
        using var handler = new RecordingHttpHandler(static _ => JsonResponse(
            HttpStatusCode.BadRequest,
            "{\"_tag\":\"InvalidRequestError\",\"message\":\"bad id\"}"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert
            .That(async () => _ = await client.Sessions.CreateSessionAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Error).IsTypeOf<InvalidRequestError>();
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
