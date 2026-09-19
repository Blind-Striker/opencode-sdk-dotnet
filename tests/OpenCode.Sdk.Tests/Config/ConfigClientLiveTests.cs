using System.Globalization;
using System.Text.Json;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;
using OpenCode.Sdk.TestSupport.Ownership;

namespace OpenCode.Sdk.Tests;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class ConfigClientLiveTests(SimulatedDriveServerFixture server)
{
    [Test]
    [Timeout(60_000)]
    public async Task GetShellsAsync_Should_Report_Acceptable_Host_Shells(CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var response = await client.Config.GetShellsAsync(cancellationToken: cancellationToken);

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Shells).IsNotEmpty();
        foreach (var shell in response.Shells)
        {
            await Assert.That(shell.Path).IsNotEmpty();
            await Assert.That(shell.Name).IsNotEmpty();
        }

        await Assert.That(response.Shells.Any(static shell => shell.Acceptable)).IsTrue();

        Console.WriteLine(
            "config-live: shells status=" + Number(response.Status) +
            " count=" + Number(response.Shells.Count) +
            " acceptable=" + Number(response.Shells.Count(static shell => shell.Acceptable)));
    }

    [Test]
    [Timeout(60_000)]
    public async Task UpdateConfigAsync_Should_Persist_And_Clear_The_Shell(CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();
        var shells = await client.Config.GetShellsAsync(cancellationToken: cancellationToken);
        var shell = shells.Shells.First(static candidate => candidate.Acceptable).Path;
        var cleanup = new OwnedCleanup(TimeSpan.FromSeconds(10));
        var cleared = false;
        Exception? failure = null;
        cleanup.Own("clear isolated shell config", async token =>
        {
            if (!cleared)
            {
                _ = await client.Experimental.UpdateConfigAsync(
                    new ExperimentalConfigUpdateRequest { Shell = null, }, cancellationToken: token);
            }
        });
        try
        {
            var set = await client.Experimental.UpdateConfigAsync(
                new ExperimentalConfigUpdateRequest { Shell = shell, }, cancellationToken: cancellationToken);
            await Assert.That(set.Status).IsEqualTo(204);
            var jsonOptions = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            };
            using var afterSet = JsonDocument.Parse(await server.ReadGlobalConfigAsync(cancellationToken), jsonOptions);
            await Assert.That(afterSet.RootElement.GetProperty("shell").GetString()).IsEqualTo(shell);

            var clear = await client.Experimental.UpdateConfigAsync(
                new ExperimentalConfigUpdateRequest { Shell = null, }, cancellationToken: cancellationToken);
            await Assert.That(clear.Status).IsEqualTo(204);
            using var afterClear = JsonDocument.Parse(await server.ReadGlobalConfigAsync(cancellationToken), jsonOptions);
            await Assert.That(afterClear.RootElement.TryGetProperty("shell", out _)).IsFalse();
            cleared = true;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await cleanup.CompleteAsync(failure);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
