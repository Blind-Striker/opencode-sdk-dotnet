using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The durable log's representative runtime corpus exercises two of 40 durable branches,
/// the log-synced watermark, and unknown-carrier behavior. Generator tests mechanically
/// prove complete converter mappings and source-generated registry coverage.
/// </summary>
public sealed class SessionLogContractTests
{
    [Test]
    public async Task GetLogAsync_Should_Type_Each_Frame_By_Its_Own_Tag()
    {
        using var scenario = ContractScenario.RespondingWithFrames(WireBodyData.Frames(
            WireBodyData.SessionCreatedEvent,
            WireBodyData.SessionDeletedEvent,
            WireBodyData.LogSyncedEvent));

        var items = await CollectAsync(scenario);

        await Assert.That(items).Count().IsEqualTo(3);
        await Assert.That(items[0]).IsTypeOf<SessionCreated>();
        await Assert.That(items[1]).IsTypeOf<SessionDeleted>();
        await Assert.That(items[2]).IsTypeOf<EventLogSynced>();
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(request.RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/experimental/session/ses_9/log"));
    }

    [Test]
    public async Task GetLogAsync_Should_Carry_Both_Durable_Envelope_Versions()
    {
        using var scenario = ContractScenario.RespondingWithFrames(WireBodyData.Frames(
            WireBodyData.SessionCreatedEvent,
            WireBodyData.SessionDeletedEvent));

        var items = await CollectAsync(scenario);

        var created = (SessionCreated)items[0];
        var deleted = (SessionDeleted)items[1];
        await Assert.That(created.Durable.Version).IsEqualTo(1d);
        await Assert.That(created.Durable.Seq).IsEqualTo(1L);
        await Assert.That(created.Data.SessionId).IsEqualTo("ses_9");
        await Assert.That(deleted.Durable.Version).IsEqualTo(2d);
        await Assert.That(deleted.Durable.AggregateId).IsEqualTo("ses_9");
    }

    [Test]
    public async Task GetLogAsync_Should_Group_Durable_Events_Apart_From_The_Watermark()
    {
        using var scenario = ContractScenario.RespondingWithFrames(WireBodyData.Frames(
            WireBodyData.SessionCreatedEvent,
            WireBodyData.LogSyncedEvent));

        var items = await CollectAsync(scenario);

        await Assert.That(items[0]).IsAssignableTo<ISessionEventDurable>();
        await Assert.That(items[1]).IsNotAssignableTo<ISessionEventDurable>();
        await Assert.That(items[1]).IsAssignableTo<ISessionLogItem>();
    }

    [Test]
    public async Task GetLogAsync_Should_Preserve_A_Tag_No_Variant_Owns()
    {
        using var scenario = ContractScenario.RespondingWithFrames(
            WireBodyData.Frames(WireBodyData.UnknownLogEvent));

        var items = await CollectAsync(scenario);

        var carrier = (UnknownSessionLogItem)items.Single();
        await Assert.That(carrier.Type).IsEqualTo("session.invented.tomorrow");
        await Assert.That(carrier.Payload.GetProperty("id").GetString()).IsEqualTo("evt_3");
    }

    [Test]
    public async Task GetLogAsync_Should_Refuse_A_Mid_Stream_Failure_Instead_Of_Yielding_It()
    {
        using var scenario = ContractScenario.RespondingWithFrames(
            WireBodyData.Frames(WireBodyData.SessionCreatedEvent)
            + WireBodyData.NamedFrame("effect/httpapi/stream/failure", WireBodyData.StreamFailureCause));

        var exception = await Assert
            .That(async () => _ = await CollectAsync(scenario))
            .Throws<OpenCodeTransportException>();

        await Assert.That(exception!.Message).Contains("Die");
    }

    [Test]
    [Arguments(400, WireBodyData.InvalidRequestError, typeof(InvalidRequestError))]
    [Arguments(401, WireBodyData.UnauthorizedError, typeof(UnauthorizedError))]
    [Arguments(404, WireBodyData.SessionNotFoundError, typeof(SessionNotFoundError))]
    public async Task GetLogAsync_Should_Throw_Each_Declared_Error_Before_Opening_The_Stream(
        int status,
        string body,
        Type expectedErrorType)
    {
        using var scenario = ContractScenario.Responding((HttpStatusCode)status, body);

        var exception = await Assert
            .That(async () => _ = await CollectAsync(scenario))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(status);
        await Assert.That(exception.Error!.GetType()).IsEqualTo(expectedErrorType);
    }

    [Test]
    public async Task GetLogAsync_Should_Compose_The_Resume_Cursor_And_Follow_Flag()
    {
        using var scenario = ContractScenario.RespondingWithFrames(
            WireBodyData.Frames(WireBodyData.LogSyncedEvent));

        _ = await CollectAsync(scenario, new SessionLogRequest { After = "12", Follow = QueryBoolean.True, });

        await Assert.That(scenario.Requests.Single().RequestUri).IsEqualTo(
            new Uri("http://localhost:4096/api/experimental/session/ses_9/log?after=12&follow=true"));
    }

    private static async Task<List<ISessionLogItem>> CollectAsync(ContractScenario scenario,
        SessionLogRequest? request = null)
    {
        var items = new List<ISessionLogItem>();
        await foreach (var item in scenario.Client.Sessions.GetSessionClient("ses_9").GetLogAsync(request))
        {
            items.Add(item);
        }

        return items;
    }
}
