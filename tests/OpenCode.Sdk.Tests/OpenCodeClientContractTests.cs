using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class OpenCodeClientContractTests
{
    [Test]
    public async Task GetHealthAsync_Should_Return_The_Typed_Payload()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.HealthOk);

        var response = await scenario.Client.GetHealthAsync();

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.RawBody).IsNull();
        await Assert.That(response.Health.Healthy).IsTrue();
        await Assert.That(response.Health.Version).IsEqualTo("0.0.0-test");
        await Assert.That(response.Health.Pid).IsEqualTo(42);
        await Assert.That(scenario.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/health"));
        await Assert.That(scenario.Requests.Single().Method).IsEqualTo(HttpMethod.Get);
    }

    [Test]
    public async Task GetHealthAsync_Should_Skip_An_Additive_Unknown_Field()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.HealthWithUnknownField);

        var response = await scenario.Client.GetHealthAsync();

        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Health.Healthy).IsTrue();
        await Assert.That(response.Health.Version).IsEqualTo("0.0.0-test");
        await Assert.That(response.Health.Pid).IsEqualTo(42);
    }

    [Test]
    [Arguments(WireBodyData.HealthMissingRequiredMember)]
    [Arguments(WireBodyData.HealthWithWrongTokenType)]
    public async Task GetHealthAsync_Should_Treat_Unmaterializable_Known_Members_As_Protocol_Failures(string body)
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, body);

        _ = await Assert
            .That(async () => _ = await scenario.Client.GetHealthAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetHealthAsync_Should_Throw_The_Typed_Error_By_Default()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.GetHealthAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(401);
        await Assert.That(exception.Error).IsTypeOf<UnauthorizedError>();
        await Assert.That(((UnauthorizedError)exception.Error!).Message).IsEqualTo("password required");
        await Assert.That(exception.RawBody).Contains("UnauthorizedError");
    }

    [Test]
    public async Task GetLocationAsync_Should_Return_The_Typed_Resolved_Location()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.ResolvedLocation);

        var response = await scenario.Client.GetLocationAsync(new LocationRequest
        {
            Location = new LocationSelector { Workspace = "wrk_1" },
        });

        await Assert.That(response.ResolvedLocation.Directory).IsEqualTo("/repo");
        await Assert.That(response.ResolvedLocation.Project.Id).IsEqualTo("prj_1");
        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/location?location[workspace]=wrk_1");
    }

    [Test]
    public async Task GetLocationAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.GetLocationAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task GetLocationAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.GetLocationAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }

    [Test]
    public async Task GetMessageAsync_Should_Unwrap_The_Data_Envelope()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session-message.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        var response = await scenario.Client.Sessions.GetSessionClient("ses_1 x").GetMessageAsync("msg_2/y");

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.Message).IsTypeOf<SessionMessageUser>();
        await Assert.That(((SessionMessageUser)response.Message).Id).IsEqualTo("msg_1");
        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/ses_1%20x/message/msg_2%2Fy");
    }

    [Test]
    public async Task GetMessageAsync_Should_Type_Both_Declared_404_Errors()
    {
        using var sessionScenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.SessionNotFoundError);
        using var messageScenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.MessageNotFoundError);

        var sessionMiss = await Assert
            .That(async () => _ = await sessionScenario.Client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();
        var messageMiss = await Assert
            .That(async () => _ = await messageScenario.Client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();

        await Assert.That(sessionMiss!.Error).IsTypeOf<SessionNotFoundError>();
        await Assert.That(messageMiss!.Error).IsTypeOf<MessageNotFoundError>();
    }

    [Test]
    public async Task GetMessageAsync_Should_Return_The_Error_Envelope_When_NoThrow()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.SessionNotFoundError);

        var response = await scenario.Client.Sessions.GetSessionClient("s").GetMessageAsync("m", OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(404);
        await Assert.That(response.Error).IsTypeOf<SessionNotFoundError>();
        await Assert.That(response.RawBody).Contains("SessionNotFoundError");
    }

    [Test]
    public async Task GetMessageAsync_Should_Downgrade_An_Unknown_Tag_To_The_Carrier()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NotFound, "{\"_tag\":\"BrandNewError\",\"detail\":7}");

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();

        var unknown = (UnknownOpenCodeError)exception!.Error!;
        await Assert.That(unknown.Tag).IsEqualTo("BrandNewError");
        await Assert.That(unknown.Payload.GetProperty("detail").GetInt32()).IsEqualTo(7);
    }

    [Test]
    public async Task GetMessageAsync_Should_Downgrade_An_Undeclared_Status_To_The_Carrier()
    {
        using var scenario = ContractScenario.Responding((HttpStatusCode)418, WireBodyData.UnauthorizedError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(418);
        await Assert.That(exception.Error).IsTypeOf<UnknownOpenCodeError>();
        await Assert.That(((UnknownOpenCodeError)exception.Error!).Tag).IsEqualTo("UnauthorizedError");
    }

    [Test]
    public async Task GetMessageAsync_Should_Downgrade_A_Known_Tag_At_The_Wrong_Status()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.SessionNotFoundError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<UnknownOpenCodeError>();
        await Assert.That(((UnknownOpenCodeError)exception.Error!).Tag).IsEqualTo("SessionNotFoundError");
    }

    [Test]
    public async Task GetHealthAsync_Should_Preserve_The_Raw_Body_For_Malformed_Errors()
    {
        const string body = "<html>not json</html>";
        using var throwScenario = ContractScenario.Responding(HttpStatusCode.BadRequest, body);

        var thrown = await Assert
            .That(async () => _ = await throwScenario.Client.GetHealthAsync())
            .Throws<OpenCodeApiException>();
        await Assert.That(thrown!.Error).IsNull();
        await Assert.That(thrown.RawBody).IsEqualTo(body);

        using var noThrowScenario = ContractScenario.Responding(HttpStatusCode.BadRequest, body);
        var response = await noThrowScenario.Client.GetHealthAsync(OpenCodeRequestOptions.NoThrow);
        await Assert.That(response.Error).IsNull();
        await Assert.That(response.RawBody).IsEqualTo(body);
    }

    [Test]
    public async Task GetHealthAsync_Should_Treat_A_Malformed_Success_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, "not json");

        _ = await Assert
            .That(async () => _ = await scenario.Client.GetHealthAsync())
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetHealthAsync_Should_Treat_An_Undeclared_2xx_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Created, WireBodyData.HealthOk);

        _ = await Assert
            .That(async () => _ = await scenario.Client.GetHealthAsync())
            .Throws<OpenCodeTransportException>();

        _ = await Assert
            .That(async () => _ = await scenario.Client.GetHealthAsync(OpenCodeRequestOptions.NoThrow))
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetHealthAsync_Should_Preserve_The_Raw_Body_For_An_Empty_Error_Tag()
    {
        const string body = "{\"_tag\":\"\"}";
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, body);

        var response = await scenario.Client.GetHealthAsync(OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Error).IsNull();
        await Assert.That(response.RawBody).IsEqualTo(body);
    }

    [Test]
    public async Task GetMessageAsync_Should_Treat_An_Empty_Success_Marker_As_A_Protocol_Failure()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope("{\"type\":\"\",\"id\":\"m\"}"));

        _ = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("s").GetMessageAsync("m"))
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task GetMessageAsync_Should_Escape_Route_Values_Into_The_Request_Uri()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session-message.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        _ = await scenario.Client.Sessions.GetSessionClient("a b").GetMessageAsync("c/d");

        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/a%20b/message/c%2Fd");
    }

    [Test]
    public async Task Payload_Should_Guard_Access_And_Printing_On_The_Error_Path()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.GetHealthAsync(OpenCodeRequestOptions.NoThrow);

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
        var client = new OpenCodeClient(new OpenCodeClientOptions { Endpoint = ContractScenario.Endpoint });
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

    [Test]
    public async Task Constructor_Should_Require_An_Endpoint_For_An_Owned_Client()
    {
        _ = Assert.Throws<ArgumentException>(() => _ = new OpenCodeClient(new OpenCodeClientOptions()));
    }

    private sealed class MockableClient : OpenCodeClient
    {
    }
}
