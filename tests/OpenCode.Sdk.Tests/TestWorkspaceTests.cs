using OpenCode.Sdk.TestSupport;
using Testably.Abstractions.Testing;

namespace OpenCode.Sdk.Tests;

public sealed class TestWorkspaceTests
{
    [Test]
    public async Task WriteTextFile_Should_Reject_A_Rooted_Path_Without_Writing_Outside_The_Workspace()
    {
        var fileSystem = new MockFileSystem();
        var runRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "test-workspace-rooted");
        using var workspace = new TestWorkspace(fileSystem, runRoot);
        var outside = fileSystem.Path.GetFullPath(
            fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "test-workspace-rooted-outside.txt"));

        var exception = await Assert
            .That(() => workspace.WriteTextFile(outside, "outside"))
            .Throws<ArgumentException>();

        await Assert.That(exception!.ParamName).IsEqualTo("relativePath");
        await Assert.That(fileSystem.File.Exists(outside)).IsFalse();
    }

    [Test]
    public async Task WriteTextFile_Should_Reject_Traversal_Without_Writing_Outside_The_Workspace()
    {
        var fileSystem = new MockFileSystem();
        var runRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "test-workspace-traversal");
        using var workspace = new TestWorkspace(fileSystem, runRoot);
        var outside = fileSystem.Path.GetFullPath(
            fileSystem.Path.Combine(workspace.Path, "..", "test-workspace-traversal-outside.txt"));

        var exception = await Assert
            .That(() => workspace.WriteTextFile("../test-workspace-traversal-outside.txt", "outside"))
            .Throws<ArgumentException>();

        await Assert.That(exception!.ParamName).IsEqualTo("relativePath");
        await Assert.That(fileSystem.File.Exists(outside)).IsFalse();
    }

    [Test]
    public async Task WriteTextFile_Should_Write_A_Valid_Nested_Path()
    {
        var fileSystem = new MockFileSystem();
        var runRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "test-workspace-valid");
        using var workspace = new TestWorkspace(fileSystem, runRoot);

        var written = workspace.WriteTextFile("nested/owned.txt", "owned content");

        var expected = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(workspace.Path, "nested", "owned.txt"));
        await Assert.That(written).IsEqualTo(expected);
        await Assert.That(fileSystem.File.Exists(expected)).IsTrue();
        using var reader = fileSystem.File.OpenText(expected);
        await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("owned content");
    }
}
