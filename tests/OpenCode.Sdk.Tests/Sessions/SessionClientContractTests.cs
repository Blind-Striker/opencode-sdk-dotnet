using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class SessionClientContractTests
{
    [Test]
    public async Task GetSessionAsync_Should_Return_The_Typed_Session()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").GetSessionAsync();

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.Session.Id).IsEqualTo("ses_100");
        await Assert.That(response.Session.Location.Directory).IsEqualTo("/repo");
        await Assert.That(response.Session.Tokens.Cache.Read).IsEqualTo(1);
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100"));
    }

    [Test]
    public async Task GetSessionAsync_Should_Treat_A_Null_Datum_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope("null"));

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_100").GetSessionAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetSessionAsync_Should_Treat_An_Explicit_Null_Parent_As_A_Malformed_Success()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.null-parent-session.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_100").GetSessionAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetSessionAsync_Should_Treat_A_Numeric_Enum_Status_As_A_Malformed_Success()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.numeric-status-session.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_100").GetSessionAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetSessionAsync_Should_Throw_The_Declared_404_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.SessionNotFoundError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_9").GetSessionAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(404);
        await Assert.That(exception.Error).IsTypeOf<SessionNotFoundError>();
    }

    [Test]
    public async Task GetSessionAsync_Should_Throw_The_Declared_401_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_9").GetSessionAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Error).IsTypeOf<UnauthorizedError>();
    }

    [Test]
    public async Task ListMessagesAsync_Should_Return_The_Typed_Page_With_Its_Cursor()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session-message.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Page(payload, previous: "cur_0"));

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").ListMessagesAsync(new MessageListRequest
        {
            Limit = 2,
            Order = ListOrder.Ascending,
        });

        await Assert.That(response.Messages.Single()).IsTypeOf<SessionMessageUser>();
        await Assert.That(response.Cursor.Previous).IsEqualTo("cur_0");
        await Assert.That(response.Cursor.Next).IsNull();
        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/ses_100/message?limit=2&order=asc");
    }

    [Test]
    public async Task ListMessagesAsync_Should_Treat_A_Null_Page_Element_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Page("null"));

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_100").ListMessagesAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ListMessagesAsync_Should_Throw_The_Declared_500_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.InternalServerError, WireBodyData.UnknownError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_100").ListMessagesAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(500);
        await Assert.That(exception.Error).IsTypeOf<UnknownError>();
        await Assert.That(((UnknownError)exception.Error!).Message).IsEqualTo("boom");
    }

    [Test]
    public async Task ListMessagesAsync_Should_Return_The_500_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.InternalServerError, WireBodyData.UnknownError);

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100")
            .ListMessagesAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(500);
        await Assert.That(response.Error).IsTypeOf<UnknownError>();
        await Assert.That(response.RawBody).Contains("boom");
    }

    [Test]
    public async Task RemoveSessionAsync_Should_Treat_The_204_As_A_Bodiless_Success()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NoContent, string.Empty);

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").RemoveSessionAsync();

        await Assert.That(response.Status).IsEqualTo(204);
        await Assert.That(response.IsError).IsFalse();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Delete);
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100"));
    }

    [Test]
    public async Task RenameSessionAsync_Should_Send_The_Typed_Body()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NoContent, string.Empty);

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").RenameSessionAsync(new SessionRenameRequest
        {
            Title = "Renamed session",
        });

        await Assert.That(response.Status).IsEqualTo(204);
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/ses_100/rename");
        await Assert.That(request.Body).IsEqualTo("{\"title\":\"Renamed session\"}");
    }

    [Test]
    public async Task ListMessagesAsync_Should_Throw_The_Declared_404_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.SessionNotFoundError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_9").ListMessagesAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Error).IsTypeOf<SessionNotFoundError>();
    }
}
