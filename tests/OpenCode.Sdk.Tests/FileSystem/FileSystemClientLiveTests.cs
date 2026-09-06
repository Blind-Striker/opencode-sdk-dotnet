using System.Globalization;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class FileSystemClientLiveTests(SimulatedDriveServerFixture server)
{
    private const string ChildDirectory = "sdk-live-child";
    private const string ChildFile = "sdk-live-child/sdk-live-find-target.txt";
    private const string RootFile = "sdk-live-root-file.txt";

    [Test]
    [Timeout(60_000)]
    public async Task FindEntriesAsync_And_ListEntriesAsync_Should_Report_The_Workspace_Entries(
        CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        _ = workspace.WriteTextFile(RootFile, "root entry");
        _ = workspace.WriteTextFile(ChildFile, "find target");
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });

        var found = await client.FileSystem.FindEntriesAsync(
            new FsFindRequest
            {
                Query = "sdk-live-find-target",
                Type = FsFindRequestType.File,
                Limit = "10",
            },
            cancellationToken: cancellationToken);

        await Assert.That(found.Status).IsEqualTo(200);
        await Assert.That(found.IsError).IsFalse();
        await Assert.That(found.Location.Directory).IsEqualTo(workspace.Path);
        var foundFile = found.Entries.Single(item => NormalizeSeparators(item.Path) == ChildFile);
        await Assert.That(foundFile.Type).IsEqualTo(FileSystemEntryType.File);

        var listed = await client.FileSystem.ListEntriesAsync(
            new FsListRequest { Path = "." }, cancellationToken: cancellationToken);

        await Assert.That(listed.Status).IsEqualTo(200);
        await Assert.That(listed.IsError).IsFalse();
        await Assert.That(listed.Location.Directory).IsEqualTo(workspace.Path);
        var rootFile = listed.Entries.Single(item => NormalizeSeparators(item.Path) == RootFile);
        await Assert.That(rootFile.Type).IsEqualTo(FileSystemEntryType.File);
        var childDirectory = listed.Entries.Single(
            item => NormalizeSeparators(item.Path) == ChildDirectory + "/");
        await Assert.That(childDirectory.Type).IsEqualTo(FileSystemEntryType.Directory);

        Console.WriteLine(
            "filesystem-live: find-status=" + Number(found.Status) +
            " find-path=" + foundFile.Path +
            " list-status=" + Number(listed.Status) +
            " directory=" + childDirectory.Path);
    }

    private static string NormalizeSeparators(string value) => value.Replace('\\', '/');

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
