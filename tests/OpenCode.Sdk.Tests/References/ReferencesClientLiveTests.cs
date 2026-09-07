using System.Globalization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class ReferencesClientLiveTests(SimulatedDriveServerFixture server)
{
    [Test]
    [Timeout(60_000)]
    public async Task ListReferencesAsync_Should_Report_The_Typed_Local_Reference(
        CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });
        var settled = await client.Plugins.AwaitPluginActivationAsync(cancellationToken: cancellationToken);
        await Assert.That(settled.Status).IsEqualTo(204);

        var response = await client.References.ListReferencesAsync(cancellationToken: cancellationToken);

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Location.Directory).IsEqualTo(workspace.Path);
        var reference = response.References.Single(item => item.Name == SimulationConfigSeed.ReferenceName);
        await Assert.That(reference.Path).IsEqualTo(workspace.Path);
        await Assert.That(reference.Description).IsEqualTo(SimulationConfigSeed.ReferenceDescription);
        await Assert.That(reference.Source).IsTypeOf<ReferenceLocalSource>();
        var local = reference.Source as ReferenceLocalSource;
        await Assert.That(local?.Path).IsEqualTo(workspace.Path);

        Console.WriteLine(
            "references-live: status=" + Number(response.Status) + " name=" + reference.Name +
            " path=" + reference.Path);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
