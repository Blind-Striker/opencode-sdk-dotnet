using System.Net;
using System.Text.Json;
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
    public async Task GetSessionAsync_Should_Collapse_An_Explicit_Null_Parent()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.null-parent-session.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").GetSessionAsync();

        await Assert.That(response.Session.ParentId).IsNull();
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
            Limit = "2",
            Order = ListOrder.Ascending,
        });

        await Assert.That(response.Messages.Single()).IsTypeOf<SessionMessageUser>();
        await Assert.That(response.Cursor.Previous).IsEqualTo("cur_0");
        await Assert.That(response.Cursor.Next).IsNull();
        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/ses_100/message?limit=2&order=asc");
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
    public async Task ListMessagesAsync_Should_Send_Order_Combined_With_Cursor()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Page(""));

        _ = await scenario.Client.Sessions.GetSessionClient("ses_100").ListMessagesAsync(new MessageListRequest
        {
            Order = ListOrder.Ascending,
            Cursor = "cur_1",
        });

        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/session/ses_100/message?order=asc&cursor=cur_1");
    }

    [Test]
    public async Task EnumerateMessagesAsync_Should_Lazily_Follow_Opaque_Next_Cursors()
    {
        const string initialCursor = "cur_start";
        const string firstNext = "cur_next_1";
        const string secondNext = "cur_next_2";
        var fixtureLoader = new FixtureLoader();
        var user = fixtureLoader.LoadJson("Serialization.known-session-message.json");
        var shell = fixtureLoader.LoadJson("Serialization.known-session-message-shell.json");
        var responses = new Queue<string>(
        [
            WireBodyData.Page("", next: firstNext),
            WireBodyData.Page(user, next: secondNext),
            WireBodyData.Page(shell),
        ]);
        using var scenario = ContractScenario.Responding(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue()),
        });
        var messages = scenario.Client.Sessions.GetSessionClient("ses_100").EnumerateMessagesAsync(new MessageListRequest
        {
            Limit = "2",
            Order = ListOrder.Ascending,
            Cursor = initialCursor,
        }, CancellationToken.None);

        await Assert.That(scenario.Requests).IsEmpty();

        var received = new List<ISessionMessageInfo>();
        await foreach (var message in messages.WithCancellation(CancellationToken.None))
        {
            received.Add(message);
        }

        await Assert.That(received.Count).IsEqualTo(2);
        await Assert.That(received[0]).IsTypeOf<SessionMessageUser>();
        await Assert.That(received[1]).IsTypeOf<SessionMessageShell>();
        await Assert
            .That(scenario.Requests.Select(static request => request.RequestUri!.AbsoluteUri).SequenceEqual(
            [
                $"http://localhost:4096/api/session/ses_100/message?limit=2&order=asc&cursor={initialCursor}",
                $"http://localhost:4096/api/session/ses_100/message?limit=2&cursor={firstNext}",
                $"http://localhost:4096/api/session/ses_100/message?limit=2&cursor={secondNext}",
            ],
            StringComparer.Ordinal))
            .IsTrue();
    }

    [Test]
    public async Task EnumerateMessagesAsync_Should_Throw_The_Declared_Error_When_A_Later_Page_Fails()
    {
        const string nextCursor = "cur_invalid";
        var payload = new FixtureLoader().LoadJson("Serialization.known-session-message.json");
        var responses = new Queue<(HttpStatusCode Status, string Body)>(
        [
            (HttpStatusCode.OK, WireBodyData.Page(payload, next: nextCursor)),
            (HttpStatusCode.BadRequest, WireBodyData.InvalidCursorError),
        ]);
        using var scenario = ContractScenario.Responding(_ =>
        {
            var response = responses.Dequeue();
            return new HttpResponseMessage(response.Status) { Content = new StringContent(response.Body), };
        });
        await using var enumerator = scenario.Client.Sessions.GetSessionClient("ses_100")
            .EnumerateMessagesAsync(cancellationToken: CancellationToken.None)
            .GetAsyncEnumerator();

        await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
        await Assert.That(enumerator.Current).IsTypeOf<SessionMessageUser>();

        var exception = await Assert
            .That(async () => _ = await enumerator.MoveNextAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception?.Error).IsTypeOf<InvalidCursorError>();
        await Assert.That(scenario.Requests.Count).IsEqualTo(2);
        await Assert.That(scenario.Requests[1].RequestUri!.Query).IsEqualTo($"?cursor={nextCursor}");
    }

    [Test]
    public async Task EnumerateMessagesAsync_Should_Observe_Cancellation_Between_Buffered_Items()
    {
        var fixtureLoader = new FixtureLoader();
        var user = fixtureLoader.LoadJson("Serialization.known-session-message.json");
        var shell = fixtureLoader.LoadJson("Serialization.known-session-message-shell.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Page($"{user},{shell}"));
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = scenario.Client.Sessions.GetSessionClient("ses_100")
            .EnumerateMessagesAsync(cancellationToken: cancellation.Token)
            .GetAsyncEnumerator();

        await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
        await cancellation.CancelAsync();

        _ = await Assert
            .That(async () => _ = await enumerator.MoveNextAsync())
            .Throws<OperationCanceledException>();
        await Assert.That(scenario.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task EnumerateMessagesAsync_Should_Send_The_Caller_Token_To_Each_Page_Request()
    {
        const string nextCursor = "cur_next";
        var payload = new FixtureLoader().LoadJson("Serialization.known-session-message.json");
        var responses = new Queue<string>(
        [
            WireBodyData.Page(payload, next: nextCursor),
            WireBodyData.Page(""),
        ]);
        using var scenario = ContractScenario.Responding(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue()),
        });
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = scenario.Client.Sessions.GetSessionClient("ses_100")
            .EnumerateMessagesAsync(cancellationToken: cancellation.Token)
            .GetAsyncEnumerator();

        await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
        await cancellation.CancelAsync();

        bool? moved = null;
        OperationCanceledException? cancellationException = null;
        try
        {
            moved = await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (OperationCanceledException exception)
        {
            cancellationException = exception;
        }

        await Assert.That(moved is false || cancellationException is not null).IsTrue();
        await Assert.That(scenario.CancellationTokens.Count).IsEqualTo(2);
        await Assert.That(scenario.CancellationTokens[1].IsCancellationRequested).IsTrue();
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
    public async Task PostForkAsync_Should_Send_The_Tagged_Boundary_Variant()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").PostForkAsync(new SessionForkPostRequest
        {
            Boundary = new SessionForkRequestBoundaryBefore { MessageId = "msg_1" },
        });

        await Assert.That(response.Fork.Id).IsEqualTo("ses_100");
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri).IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100/fork"));
        await Assert.That(request.Body).IsEqualTo("{\"boundary\":{\"type\":\"before\",\"messageID\":\"msg_1\"}}");
    }

    [Test]
    public async Task PostCompactAsync_Should_Send_The_Empty_Body_When_Omitted()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-inbox-compaction.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").PostCompactAsync();

        await Assert.That(response.Compact.Id).IsEqualTo("inb_1");
        await Assert.That(response.Compact.Delivery).IsEqualTo(SessionInboxDelivery.Queue);
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.ContentType).IsEqualTo("application/json");
        await Assert.That(request.Body).IsEqualTo("{}");
    }

    [Test]
    public async Task GetExportAsync_Should_Compose_The_Sanitize_Query()
    {
        var session = new FixtureLoader().LoadJson("Serialization.known-session.json");
        using var scenario = ContractScenario.Responding(
            HttpStatusCode.OK,
            WireBodyData.Envelope($"{{\"info\":{session},\"messages\":[]}}"));

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").GetExportAsync(new SessionExportRequest
        {
            Sanitize = QueryBoolean.True,
        });

        await Assert.That(response.Export.Info.Id).IsEqualTo("ses_100");
        await Assert.That(response.Export.Messages).IsEmpty();
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100/export?sanitize=true"));
    }

    [Test]
    public async Task DeleteInboxCancelAsync_Should_Compose_Both_Path_Parameters_On_The_204()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NoContent, string.Empty);

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").DeleteInboxCancelAsync("inb_1");

        await Assert.That(response.Status).IsEqualTo(204);
        await Assert.That(response.IsError).IsFalse();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Delete);
        await Assert.That(request.RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100/inbox/inb_1"));
    }

    [Test]
    public async Task DeleteInboxCancelAsync_Should_Throw_The_Declared_409_Conflict()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Conflict, WireBodyData.ConflictError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_100").DeleteInboxCancelAsync("inb_1"))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(409);
        await Assert.That(exception.Error).IsTypeOf<ConflictError>();
    }

    [Test]
    public async Task PostInterruptAsync_Should_Send_A_Bodiless_Post()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.SessionInterrupted);

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").PostInterruptAsync();

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Interrupt.Interrupted).IsTrue();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100/interrupt"));
        await Assert.That(request.Body).IsNull();
        await Assert.That(request.ContentType).IsNull();
    }

    [Test]
    public async Task PostFormCancelAsync_Should_Throw_The_Declared_409_Error()
    {
        using var scenario = ContractScenario.Responding(
            HttpStatusCode.Conflict,
            "{\"_tag\":\"FormAlreadySettledError\",\"id\":\"frm_1\",\"message\":\"settled\"}");

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_100").PostFormCancelAsync("frm_1"))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(409);
        await Assert.That(exception.Error).IsTypeOf<FormAlreadySettledError>();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100/form/frm_1/cancel"));
        await Assert.That(request.Body).IsNull();
    }

    [Test]
    public async Task PutInstructionsEntryAsync_Should_Send_The_Typed_Body_On_The_Put()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NoContent, string.Empty);
        using var value = JsonDocument.Parse("\"be terse\"");

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").PutInstructionsEntryAsync(
            "style",
            new SessionInstructionsEntryPutRequest { Value = value.RootElement });

        await Assert.That(response.Status).IsEqualTo(204);
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Put);
        await Assert.That(request.RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100/instructions/entries/style"));
        await Assert.That(request.Body).IsEqualTo("{\"value\":\"be terse\"}");
    }

    [Test]
    public async Task PutInstructionsEntryAsync_Should_Throw_The_Declared_413_Error()
    {
        using var scenario = ContractScenario.Responding(
            HttpStatusCode.RequestEntityTooLarge,
            "{\"_tag\":\"InstructionEntryValueTooLargeError\",\"actualBytes\":2048,\"maxBytes\":1024,\"message\":\"too large\"}");
        using var value = JsonDocument.Parse("\"be terse\"");

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_100").PutInstructionsEntryAsync(
                "style",
                new SessionInstructionsEntryPutRequest { Value = value.RootElement }))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(413);
        var error = (InstructionEntryValueTooLargeError)exception.Error!;
        await Assert.That(error.ActualBytes).IsEqualTo(2048);
        await Assert.That(error.MaxBytes).IsEqualTo(1024);
    }

    [Test]
    public async Task CreatePermissionAsync_Should_Send_The_Typed_Body_And_Return_The_Effect()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-permission.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        var response = await scenario.Client.Sessions.GetSessionClient("ses_100").CreatePermissionAsync(
            new SessionPermissionCreateRequest
            {
                Action = "read",
                Resources = ["file:///repo/a.txt"],
            });

        await Assert.That(response.Permission.Id).IsEqualTo("perm_1");
        await Assert.That(response.Permission.Effect).IsEqualTo(PermissionEffect.Allow);
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/session/ses_100/permission"));
        await Assert.That(request.Body).IsEqualTo("{\"action\":\"read\",\"resources\":[\"file:///repo/a.txt\"]}");
    }

    [Test]
    public async Task GetPermissionAsync_Should_Throw_The_Declared_404_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.NotFound, WireBodyData.PermissionNotFoundError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.Sessions.GetSessionClient("ses_100").GetPermissionAsync("req_9"))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(404);
        await Assert.That(exception.Error).IsTypeOf<PermissionNotFoundError>();
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
