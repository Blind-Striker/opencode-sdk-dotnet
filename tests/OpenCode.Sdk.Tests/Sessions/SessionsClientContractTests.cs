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
            Limit = "5",
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
    public async Task ListSessionsAsync_Should_Treat_A_Null_Page_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "{\"data\":null,\"cursor\":{}}");

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.ListSessionsAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ListSessionsAsync_Should_Send_The_OpenApi_String_Limit_Without_Local_Validation()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Page(""));

        _ = await scenario.Client.Sessions.ListSessionsAsync(new SessionListRequest { Limit = "not-a-number" });

        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session?limit=not-a-number");
    }

    [Test]
    [Arguments(WireBodyData.InvalidCursorError, typeof(InvalidCursorError))]
    [Arguments(WireBodyData.InvalidRequestError, typeof(InvalidRequestError))]
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
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.CreateSessionAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task GetActiveAsync_Should_Return_The_Typed_Active_Sessions()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope("{\"ses_100\":{\"type\":\"running\"}}"));

        var response = await scenario.Client.Sessions.GetActiveAsync();

        await Assert.That(response.Active.Count).IsEqualTo(1);
        await Assert.That(response.Active["ses_100"].Type).IsEqualTo("running");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/session/active"));
    }

    [Test]
    public async Task GetActiveAsync_Should_Return_An_Empty_Map_When_No_Sessions_Are_Active()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope("{}"));

        var response = await scenario.Client.Sessions.GetActiveAsync();

        await Assert.That(response.Active).IsEmpty();
    }

    [Test]
    public async Task GetActiveAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetActiveAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task GetActiveAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Sessions.GetActiveAsync(OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }

    [Test]
    public async Task GetStatsAsync_Should_Return_The_Typed_Stats_With_Their_Detailed_Tool_Usage()
    {
        var stats = new FixtureLoader().LoadJson("Serialization.known-session-stats.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(stats));

        var response = await scenario.Client.Sessions.GetStatsAsync();

        await Assert.That(response.Stats.Sessions).IsEqualTo(12);
        await Assert.That(response.Stats.Range.From).IsEqualTo(1_735_689_600d);
        await Assert.That(response.Stats.Tokens.Cache.Read).IsEqualTo(256d);
        await Assert.That(response.Stats.Cost).IsEqualTo(1.25d);
        await Assert.That(response.Stats.Activity.Count).IsEqualTo(2);
        await Assert.That(response.Stats.Models.Single().Model.ProviderId).IsEqualTo("anthropic");
        await Assert.That(response.Stats.Tools).IsTypeOf<SessionStatsToolsDetail>();
        var tools = (SessionStatsToolsDetail)response.Stats.Tools;
        await Assert.That(tools.Totals.Calls).IsEqualTo(20);
        await Assert.That(tools.Usage[0].Name).IsEqualTo("bash");
        await Assert.That(tools.Usage[1].DurationP50).IsNull();
        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/stats");
    }

    [Test]
    public async Task GetStatsAsync_Should_Send_The_Tools_Enum_As_Its_Wire_Value()
    {
        var stats = new FixtureLoader().LoadJson("Serialization.known-session-stats.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(stats));

        _ = await scenario.Client.Sessions.GetStatsAsync(new SessionStatsRequest
        {
            From = "2026-08-01",
            Tools = SessionStatsRequestTools.Summary,
        });

        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/stats?from=2026-08-01&tools=summary");
    }

    [Test]
    public async Task GetStatsAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetStatsAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task GetStatsAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.Sessions.GetStatsAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }
}
