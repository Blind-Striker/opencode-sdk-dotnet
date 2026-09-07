using System.Globalization;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class ProjectsClientLiveTests(SimulatedDriveServerFixture server)
{
    private const string OwnerFile = "sdk-live-project-owner.txt";

    [Test]
    [Timeout(60_000)]
    public async Task GetCurrentAsync_And_ListProjectsAsync_Should_Report_The_Workspace_Project(
        CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        var owner = Guid.NewGuid().ToString("N");
        _ = workspace.WriteTextFile(OwnerFile, owner);
        using var client = server.CreateClient();

        var current = await client.Projects.GetCurrentAsync(
            new ProjectCurrentRequest
            {
                Location = new LocationSelector { Directory = workspace.Path },
            },
            cancellationToken: cancellationToken);

        await Assert.That(current.Status).IsEqualTo(200);
        await Assert.That(current.IsError).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(current.Current.Id)).IsFalse();
        await Assert.That(current.Current.Canonical).IsEqualTo(current.Current.Directory);
        await Assert.That(workspace.HasTextFile(current.Current.Directory, OwnerFile, owner)).IsTrue();

        var listed = await client.Projects.ListProjectsAsync(cancellationToken: cancellationToken);

        await Assert.That(listed.Status).IsEqualTo(200);
        await Assert.That(listed.IsError).IsFalse();
        var project = listed.Projects.Single(item => item.Id == current.Current.Id);
        await Assert.That(project.Canonical).IsEqualTo(current.Current.Canonical);
        await Assert.That(workspace.HasTextFile(project.Canonical, OwnerFile, owner)).IsTrue();

        Console.WriteLine(
            "projects-live: current=" + Number(current.Status) +
            " list=" + Number(listed.Status) +
            " id=" + project.Id +
            " directory=" + current.Current.Directory);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
