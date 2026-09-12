using System.Globalization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Sessions;

/// <summary>
/// The session permission ruleset's live proof against the pinned server: the PUT answers the
/// declared 204 and the session the rules were written to reports them back, so the operation is
/// verified by the state it leaves rather than by its status alone.
/// </summary>
[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class SessionPermissionRulesLiveTests(SimulatedDriveServerFixture server)
{
    private const string DeniedResource = "sdk-live-denied";
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);

    [Test]
    [Timeout(120_000)]
    public async Task PutPermissionRulesAsync_Should_Replace_The_Session_Ruleset(CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });
        var created = await client.Sessions.CreateSessionAsync(
            new SessionCreateRequest
            {
                Title = "session-permission-rules-live",
                Location = new LocationRef { Directory = workspace.Path },
            },
            cancellationToken: cancellationToken);
        await Assert.That(created.Status).IsEqualTo(200);
        var session = client.Sessions.GetSessionClient(created.Session.Id);
        Exception? primaryFailure = null;

        try
        {
            var response = await session.PutPermissionRulesAsync(
                new SessionPermissionRulesPutRequest
                {
                    Permissions =
                    [
                        new PermissionRule
                        {
                            Action = SimulationConfigSeed.PermissionProbeAction,
                            Resource = DeniedResource,
                            Effect = PermissionEffect.Deny,
                        },
                    ],
                },
                cancellationToken: cancellationToken);

            await Assert.That(response.Status).IsEqualTo(204);
            await Assert.That(response.IsError).IsFalse();

            var reread = await session.GetSessionAsync(cancellationToken: cancellationToken);

            await Assert.That(reread.Status).IsEqualTo(200);
            await Assert.That(reread.Session.Permissions).IsNotNull();
            var rule = reread.Session.Permissions!.Single(static candidate => candidate.Resource == DeniedResource);
            await Assert.That(rule.Action).IsEqualTo(SimulationConfigSeed.PermissionProbeAction);
            await Assert.That(rule.Effect).IsEqualTo(PermissionEffect.Deny);

            Console.WriteLine(
                "session-permission-rules-live: put=" + Number(response.Status) +
                " reread=" + Number(reread.Status) +
                " rules=" + Number(reread.Session.Permissions.Count));
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            await CleanupAsync(session, primaryFailure);
        }
    }

    private static async Task CleanupAsync(SessionClient session, Exception? primaryFailure)
    {
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        try
        {
            _ = await session.RemoveSessionAsync(cancellationToken: cleanup.Token);
        }
        catch (Exception exception) when (primaryFailure is not null)
        {
            Console.WriteLine("session-permission-rules-live: cleanup suppressed " + exception.GetType().Name);
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
