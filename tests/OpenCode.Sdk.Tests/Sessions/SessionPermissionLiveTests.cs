using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Sessions;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class SessionPermissionLiveTests(SimulatedDriveServerFixture server)
{
    private const string PermissionResource = "resource:one";
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);

    [Test]
    [Timeout(180_000)]
    public async Task CreatePermissionAsync_Should_Save_An_Always_Grant_And_Consume_The_Request(
        CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });
        var location = (await client.GetLocationAsync(cancellationToken: cancellationToken)).ResolvedLocation;
        await AssertPermissionProbeAsync(client, location.Directory, cancellationToken);

        var sessionId = await CreateOwnedSessionAsync(client, workspace.Path, cancellationToken);
        var session = client.Sessions.GetSessionClient(sessionId);
        var cleanupState = new PermissionCleanupState();
        var cleanup = CreateCleanup(session, client, location.Project.Id, cleanupState);
        Exception? primaryFailure = null;

        try
        {
            var createdPermission = await session.CreatePermissionAsync(
                new SessionPermissionCreateRequest
                {
                    Action = SimulationConfigSeed.PermissionProbeAction,
                    Resources = [PermissionResource],
                    Save = [PermissionResource],
                    Agent = SimulationConfigSeed.PermissionProbeAgentId,
                },
                cancellationToken: cancellationToken);
            cleanupState.PermissionId = createdPermission.Permission.Id;

            await Assert.That(createdPermission.Permission.Effect).IsEqualTo(PermissionEffect.Ask);

            var pending = (await session.GetPermissionAsync(
                cleanupState.PermissionId,
                cancellationToken: cancellationToken)).Permission;
            await AssertPermissionRequestAsync(pending, cleanupState.PermissionId, sessionId);

            var listed = await session.ListRequestsAsync(cancellationToken: cancellationToken);
            var listedMatches = listed.Requests.Where(request => request.Id == cleanupState.PermissionId).ToList();
            await Assert.That(listedMatches).Count().IsEqualTo(1);
            await AssertPermissionRequestAsync(listedMatches[0], cleanupState.PermissionId, sessionId);

            var replied = await session.PostPermissionReplyAsync(
                cleanupState.PermissionId,
                new SessionPermissionReplyPostRequest { Reply = PermissionReply.Always },
                cancellationToken: cancellationToken);
            await Assert.That(replied.Status).IsEqualTo(204);

            var consumed = await session.GetPermissionAsync(
                cleanupState.PermissionId,
                OpenCodeRequestOptions.NoThrow,
                cancellationToken);
            await Assert.That(consumed.Status).IsEqualTo(404);
            await Assert.That(consumed.Error).IsTypeOf<PermissionNotFoundError>();
            var notFound = consumed.Error as PermissionNotFoundError;
            await Assert.That(notFound?.RequestId).IsEqualTo(cleanupState.PermissionId);

            var saved = await client.Permissions.ListSavedAsync(
                new PermissionSavedListRequest { ProjectId = location.Project.Id },
                cancellationToken: cancellationToken);
            var savedMatches = OwnedSavedPermissions(saved.Saved, location.Project.Id);
            await Assert.That(savedMatches).Count().IsEqualTo(1);
            cleanupState.SavedId = savedMatches[0].Id;

            var removed = await client.Permissions.RemoveSavedAsync(
                cleanupState.SavedId,
                cancellationToken: cancellationToken);
            await Assert.That(removed.Status).IsEqualTo(204);

            var afterRemoval = await client.Permissions.ListSavedAsync(
                new PermissionSavedListRequest { ProjectId = location.Project.Id },
                cancellationToken: cancellationToken);
            await Assert.That(afterRemoval.Saved.Any(info => info.Id == cleanupState.SavedId)).IsFalse();
            await Assert.That(OwnedSavedPermissions(afterRemoval.Saved, location.Project.Id)).IsEmpty();

            WriteEvidence(consumed.Status, removed.Status);
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

    private static async Task AssertPermissionProbeAsync(
        OpenCodeClient client,
        string directory,
        CancellationToken cancellationToken)
    {
        var activation = await client.Plugins.AwaitPluginActivationAsync(
            new PluginAwaitActivationPostRequest
            {
                Location = new LocationSelector { Directory = directory },
            },
            cancellationToken: cancellationToken);
        await Assert.That(activation.Status).IsEqualTo(204);
        var agent = (await client.Agents.GetAgentAsync(
            SimulationConfigSeed.PermissionProbeAgentId,
            cancellationToken: cancellationToken)).Agent;
        await Assert.That(agent.Id).IsEqualTo(SimulationConfigSeed.PermissionProbeAgentId);
        await Assert.That(agent.Permissions.Any(rule =>
            rule.Action == SimulationConfigSeed.PermissionProbeAction &&
            rule.Resource == "*" &&
            rule.Effect == PermissionEffect.Ask)).IsTrue();
    }

    private static async Task<string> CreateOwnedSessionAsync(
        OpenCodeClient client,
        string directory,
        CancellationToken cancellationToken)
    {
        var created = await client.Sessions.CreateSessionAsync(
            new SessionCreateRequest
            {
                Title = "session-permission-live",
                Agent = SimulationConfigSeed.PermissionProbeAgentId,
                Location = new LocationRef { Directory = directory },
            },
            cancellationToken: cancellationToken);
        return created.Session.Id;
    }

    private static OwnedSessionCleanup CreateCleanup(
        SessionClient session,
        OpenCodeClient client,
        string projectId,
        PermissionCleanupState state)
    {
        var cleanup = new OwnedSessionCleanup(session, CleanupTimeout);
        cleanup.MarkTurnCompleted();
        cleanup.Own(
            "pending permission request",
            async token =>
            {
                if (state.PermissionId is not null)
                {
                    _ = await session.PostPermissionReplyAsync(
                        state.PermissionId,
                        new SessionPermissionReplyPostRequest { Reply = PermissionReply.Reject },
                        OpenCodeRequestOptions.NoThrow,
                        token);
                }
            });
        cleanup.Own(
            "saved permission discovery",
            async token =>
            {
                var saved = await client.Permissions.ListSavedAsync(
                    new PermissionSavedListRequest { ProjectId = projectId },
                    cancellationToken: token);
                state.SavedId ??= OwnedSavedPermissions(saved.Saved, projectId).SingleOrDefault()?.Id;
            });
        cleanup.Own(
            "saved permission removal",
            async token =>
            {
                if (state.SavedId is not null)
                {
                    _ = await client.Permissions.RemoveSavedAsync(state.SavedId, cancellationToken: token);
                }
            });
        return cleanup;
    }

    private static async Task AssertPermissionRequestAsync(
        PermissionRequest request,
        string permissionId,
        string sessionId)
    {
        await Assert.That(request.Id).IsEqualTo(permissionId);
        await Assert.That(request.SessionId).IsEqualTo(sessionId);
        await Assert.That(request.Action).IsEqualTo(SimulationConfigSeed.PermissionProbeAction);
        await Assert.That(request.Resources).IsEquivalentTo([PermissionResource]);
        await Assert.That(request.Save).IsEquivalentTo([PermissionResource]);
    }

    private static List<PermissionSavedInfo> OwnedSavedPermissions(
        IEnumerable<PermissionSavedInfo> saved,
        string projectId) =>
        [.. saved.Where(info =>
            info.ProjectId == projectId &&
            info.Action == SimulationConfigSeed.PermissionProbeAction &&
            info.Resource == PermissionResource)];

    private static void WriteEvidence(int consumedStatus, int removedStatus) =>
        Console.WriteLine(
            "session-permission-live: effect=ask pending-consumed-status=" + consumedStatus.ToString(
                System.Globalization.CultureInfo.InvariantCulture) +
            " saved-grant-removed-status=" + removedStatus.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

    private sealed class PermissionCleanupState
    {
        public string? PermissionId { get; set; }

        public string? SavedId { get; set; }
    }
}
