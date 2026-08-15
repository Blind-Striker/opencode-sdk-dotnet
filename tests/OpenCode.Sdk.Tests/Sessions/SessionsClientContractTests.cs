using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class SessionsClientContractTests
{
    [Test]
    public async Task ListSessionsAsync_Should_Return_The_Typed_Page_With_Its_Cursor()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Page(payload, next: "cur_2"));

        var response = await scenario.Client.Sessions.ListSessionsAsync();

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.Sessions.Single().Id).IsEqualTo("ses_100");
        await Assert.That(response.Sessions.Single().Title).IsEqualTo("Fix the build");
        await Assert.That(response.Cursor.Next).IsEqualTo("cur_2");
        await Assert.That(response.Cursor.Previous).IsNull();
        await Assert.That(scenario.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/session"));
    }

    [Test]
    public async Task ListSessionsAsync_Should_Compose_The_Escaped_Query()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Page(""));

        _ = await scenario.Client.Sessions.ListSessionsAsync(new SessionListRequest
        {
            Limit = 5,
            Order = ListOrder.Descending,
            Search = "a b",
            ParentId = SessionParentFilter.RootOnly,
            Cursor = "cur_1",
        });

        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo(
            "http://localhost:4096/api/session?limit=5&order=desc&search=a%20b&parentID=null&cursor=cur_1");
    }

    [Test]
    [Arguments(" ")]
    [Arguments(".")]
    [Arguments("..")]
    public async Task GetSessionClient_Should_Refuse_An_Id_The_Route_Would_Refuse(string sessionId)
    {
        using var scenario = ContractScenario.Responding();

        var exception = Assert.Throws<ArgumentException>(() => _ = scenario.Client.Sessions.GetSessionClient(sessionId));

        await Assert.That(exception.ParamName).IsEqualTo("sessionId");
    }

    [Test]
    public async Task ListSessionsAsync_Should_Treat_A_Null_Page_Element_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Page("null"));

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.ListSessionsAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ListSessionsAsync_Should_Treat_A_Null_Page_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "{\"data\":null,\"cursor\":{}}");

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.ListSessionsAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ListSessionsAsync_Should_Refuse_A_Non_Positive_Limit_Before_Sending()
    {
        using var scenario = ContractScenario.Responding();

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.ListSessionsAsync(new SessionListRequest { Limit = 0 }))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(exception!.ParamName).IsEqualTo("request");
        await Assert.That(scenario.Requests).IsEmpty();
    }

    [Test]
    [Arguments("{\"_tag\":\"InvalidCursorError\",\"message\":\"stale\"}", typeof(InvalidCursorError))]
    [Arguments("{\"_tag\":\"InvalidRequestError\",\"message\":\"bad\"}", typeof(InvalidRequestError))]
    public async Task ListSessionsAsync_Should_Type_Both_Declared_400_Errors(string body, Type expected)
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, body);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.ListSessionsAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error!.GetType()).IsEqualTo(expected);
    }

    [Test]
    public async Task ListSessionsAsync_Should_Treat_An_Undeclared_Success_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Accepted, "{}");

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.ListSessionsAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ListSessionsAsync_Should_Treat_A_Missing_Envelope_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "{}");

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.ListSessionsAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task CreateSessionAsync_Should_Send_The_Empty_Body_When_Omitted()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        var response = await scenario.Client.Sessions.CreateSessionAsync();

        await Assert.That(response.Session.Id).IsEqualTo("ses_100");
        await Assert.That(scenario.Requests.Single().Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(scenario.Requests.Single().ContentType).IsEqualTo("application/json");
        await Assert.That(scenario.Requests.Single().Body).IsEqualTo("{}");
    }

    [Test]
    public async Task CreateSessionAsync_Should_Send_The_Typed_Body()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        _ = await scenario.Client.Sessions.CreateSessionAsync(new SessionCreateRequest
        {
            Title = "Fresh session",
        });

        await Assert.That(scenario.Requests.Single().Body).IsEqualTo("{\"title\":\"Fresh session\"}");
    }

    [Test]
    public async Task CreateSessionAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(
            HttpStatusCode.BadRequest,
            "{\"_tag\":\"InvalidRequestError\",\"message\":\"bad id\"}");

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.CreateSessionAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Error).IsTypeOf<InvalidRequestError>();
    }
}
