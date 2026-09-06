using System.Globalization;
using System.Text.Json;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Sessions;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class SessionsClientLiveTests(SimulatedDriveServerFixture server)
{
    private const string ModelId = "sim-model";
    private const string ProviderId = "sim";
    private const string TransferPrompt = "task-two raw transfer prompt";
    private const string TransferReply = "Task two raw transfer reply.";
    private const string StatsPrompt = "task-two statistics prompt";
    private const string StatsReply = "Task two statistics reply.";
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);

    [Test]
    [Timeout(180_000)]
    public async Task PostImportAsync_Should_Preserve_The_Raw_Exported_Session(
        CancellationToken cancellationToken)
    {
        using var source = server.CreateWorkspace();
        using var destination = server.CreateWorkspace();
        using var sourceClient = server.CreateClient(new LocationSelector { Directory = source.Path });
        using var destinationClient = server.CreateClient(new LocationSelector { Directory = destination.Path });
        var created = await CreateOwnedSessionAsync(
            sourceClient, source.Path, "session-transfer-live", cancellationToken);
        var session = sourceClient.Sessions.GetSessionClient(created.Id);
        var cleanup = CreateCleanup(session, created.Id);
        Exception? primaryFailure = null;

        try
        {
            var turn = new SimulatedSessionTurn(server, sourceClient, session, created.Id);
            _ = await turn.CompleteAsync(TransferPrompt, TransferReply, cancellationToken);
            cleanup.MarkTurnCompleted();
            var exported = await session.GetExportAsync(
                new SessionExportRequest { Sanitize = QueryBoolean.False },
                cancellationToken: cancellationToken);
            var exportedEvidence = await AssertRawExportAsync(exported, created.Id);
            var importRequest = CreateImportRequest(exported.Export, destination.Path);

            await AssertDuplicateImportConflictAsync(
                destinationClient, importRequest, created.Id, cancellationToken);

            var removed = await session.RemoveSessionAsync(cancellationToken: cancellationToken);
            await Assert.That(removed.Status).IsEqualTo(204);
            await Assert.That(removed.IsError).IsFalse();

            var imported = await destinationClient.Sessions.PostImportAsync(
                importRequest, cancellationToken: cancellationToken);
            await Assert.That(imported.Status).IsEqualTo(200);
            await Assert.That(imported.IsError).IsFalse();
            await Assert.That(imported.Import.Id).IsEqualTo(created.Id);
            await Assert.That(imported.Import.Location.Directory).IsEqualTo(destination.Path);

            var importedMessages = await destinationClient.Sessions.GetSessionClient(created.Id).ListMessagesAsync(
                new MessageListRequest { Order = ListOrder.Ascending },
                cancellationToken: cancellationToken);
            await Assert.That(importedMessages.Status).IsEqualTo(200);
            await Assert.That(importedMessages.IsError).IsFalse();
            var importedEvidence = ProjectMessages(importedMessages.Messages);
            await Assert.That(importedEvidence.SequenceEqual(exportedEvidence)).IsTrue();

            Console.WriteLine(
                "session-transfer-live: export=" + Number(exported.Status) +
                " duplicate=409 import=" + Number(imported.Status) +
                " messages=" + Number(importedEvidence.Count) + " sanitize=false");
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            await cleanup.CompleteAsync(primaryFailure);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task GetStatsAsync_Should_Report_Positive_Owned_Session_Activity(
        CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });
        var created = await CreateOwnedSessionAsync(client, workspace.Path, "session-stats-live", cancellationToken);
        var session = client.Sessions.GetSessionClient(created.Id);
        var cleanup = CreateCleanup(session, created.Id);
        Exception? primaryFailure = null;

        try
        {
            var turn = new SimulatedSessionTurn(server, client, session, created.Id);
            _ = await turn.CompleteAsync(StatsPrompt, StatsReply, cancellationToken);
            cleanup.MarkTurnCompleted();
            var messages = await session.ListMessagesAsync(
                new MessageListRequest { Order = ListOrder.Ascending },
                cancellationToken: cancellationToken);
            var observed = ProjectMessages(messages.Messages);
            await Assert.That(observed.Count).IsGreaterThan(0);
            var from = observed.Min(message => message.Created) - 1;
            var to = observed.Max(message => message.Created) + 1;

            var response = await client.Sessions.GetStatsAsync(new SessionStatsRequest
            {
                From = Number(from),
                To = Number(to),
                Project = created.ProjectId,
            }, cancellationToken: cancellationToken);

            await Assert.That(response.Status).IsEqualTo(200);
            await Assert.That(response.IsError).IsFalse();
            await Assert.That(response.Stats.Range.From).IsEqualTo(from);
            await Assert.That(response.Stats.Range.To).IsEqualTo(to);
            await Assert.That(response.Stats.Sessions).IsGreaterThan(0);
            await Assert.That(response.Stats.Prompts).IsGreaterThan(0);
            await Assert.That(response.Stats.Steps).IsGreaterThan(0);
            await Assert.That(response.Stats.Models.Any(model =>
                model.Model is { Id: ModelId, ProviderId: ProviderId })).IsTrue();

            Console.WriteLine(
                "session-stats-live: sessions=" + Number(response.Stats.Sessions) +
                " prompts=" + Number(response.Stats.Prompts) +
                " steps=" + Number(response.Stats.Steps) + " model=sim/sim-model");
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            await cleanup.CompleteAsync(primaryFailure);
        }
    }

    private static async Task<SessionInfo> CreateOwnedSessionAsync(
        OpenCodeClient client,
        string directory,
        string title,
        CancellationToken cancellationToken)
    {
        var response = await client.Sessions.CreateSessionAsync(new SessionCreateRequest
        {
            Title = title,
            Location = new LocationRef { Directory = directory },
            Model = new ModelRef { Id = ModelId, ProviderId = ProviderId },
        }, cancellationToken: cancellationToken);
        return response.Session;
    }

    private static OwnedSessionCleanup CreateCleanup(SessionClient session, string sessionId)
    {
        var cleanup = new OwnedSessionCleanup(
            async token => _ = await session.PostInterruptAsync(cancellationToken: token),
            token => RemoveOwnedSessionAsync(session, sessionId, token),
            CleanupTimeout);
        cleanup.MarkTurnStarted();
        return cleanup;
    }

    private static async Task<List<(string Type, string Id, string Text, double Created)>> AssertRawExportAsync(
        SessionExportResponse response,
        string sessionId)
    {
        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Export.Info.Id).IsEqualTo(sessionId);
        await Assert.That(response.Export.Messages.Count).IsGreaterThan(0);
        await Assert.That(response.Export.Messages.OfType<SessionMessageUser>().Any()).IsTrue();
        await Assert.That(response.Export.Messages.OfType<SessionMessageAssistant>().Any()).IsTrue();
        var evidence = ProjectMessages(response.Export.Messages);
        await Assert.That(evidence.Any(message =>
            message is { Type: "user", Text: TransferPrompt })).IsTrue();
        await Assert.That(evidence.Any(message =>
            message is { Type: "assistant", Text: TransferReply })).IsTrue();
        return evidence;
    }

    private static async Task AssertDuplicateImportConflictAsync(
        OpenCodeClient destinationClient,
        SessionImportPostRequest request,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var response = await destinationClient.Sessions.PostImportAsync(
            request, OpenCodeRequestOptions.NoThrow, cancellationToken);

        await Assert.That(response.Status).IsEqualTo(409);
        await Assert.That(response.Error).IsTypeOf<ConflictError>();
        var conflict = response.Error as ConflictError;
        await Assert.That(conflict?.Tag).IsEqualTo("ConflictError");
        await Assert.That(conflict?.Resource).IsEqualTo(sessionId);
    }

    private static SessionImportPostRequest CreateImportRequest(
        SessionTransferData exported,
        string destination) =>
        new()
        {
            Info = exported.Info,
            Messages = exported.Messages,
            Location = new LocationRef { Directory = destination },
        };

    private static List<(string Type, string Id, string Text, double Created)> ProjectMessages(
        IEnumerable<ISessionMessageInfo> messages)
    {
        var serializer = new GeneratedJsonSerializer();
        var evidence = new List<(string Type, string Id, string Text, double Created)>();
        foreach (var message in messages)
        {
            using var document = JsonDocument.Parse(serializer.Serialize(message));
            var root = document.RootElement;
            evidence.Add((
                root.GetProperty("type").GetString()!,
                root.GetProperty("id").GetString()!,
                ReadText(root),
                root.GetProperty("time").GetProperty("created").GetDouble()));
        }

        return evidence;
    }

    private static string ReadText(JsonElement message)
    {
        if (message.TryGetProperty("text", out var text))
        {
            return text.GetString()!;
        }

        if (!message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        return string.Concat(content.EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "text")
            .Select(item => item.GetProperty("text").GetString()));
    }

    private static async Task RemoveOwnedSessionAsync(
        SessionClient session,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var response = await session.RemoveSessionAsync(OpenCodeRequestOptions.NoThrow, cancellationToken);
        if (response.Status == 204
            || (response is { Status: 404, Error: SessionNotFoundError missing }
                && missing.SessionId == sessionId))
        {
            return;
        }

        throw new InvalidOperationException(
            "Owned session cleanup returned status " + Number(response.Status) +
            ", error " + response.Error?.GetType().Name + ", body " + response.RawBody + ".");
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
