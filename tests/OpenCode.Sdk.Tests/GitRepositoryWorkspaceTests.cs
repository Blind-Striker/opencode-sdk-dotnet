using System.IO.Abstractions;
using NSubstitute;
using OpenCode.Sdk.TestSupport;
using OpenCode.Sdk.TestSupport.Abstractions;
using Testably.Abstractions.Testing;

namespace OpenCode.Sdk.Tests;

public sealed class GitRepositoryWorkspaceTests
{
    [Test]
    public async Task CreateAsync_Should_Preserve_Initialization_And_Cleanup_Failures()
    {
        var cleanupFailure = new InvalidOperationException("Owned workspace cleanup failed.");
        var initializationFailure = new InvalidOperationException("Git initialization failed.");
        var inner = new MockFileSystem();
        var directory = Substitute.For<IDirectory>();
        directory.CreateDirectory(Arg.Any<string>()).Returns(call =>
            inner.Directory.CreateDirectory(call.Arg<string>()!));
        directory.When(value => value.Delete(Arg.Any<string>(), recursive: true)).Do(_ => throw cleanupFailure);
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Returns(directory);
        fileSystem.File.Returns(inner.File);
        fileSystem.Path.Returns(inner.Path);
        var gitProcess = Substitute.For<IGitProcess>();
        gitProcess.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(initializationFailure));

        var caught = await Assert.That(async () => _ = await GitRepositoryWorkspace.CreateAsync(
            fileSystem,
            gitProcess,
            inner.Path.Combine(inner.Path.GetTempPath(), "run"),
            CancellationToken.None)).Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(caught, initializationFailure)).IsTrue();
        await Assert.That(caught!.Data[GitRepositoryWorkspace.CleanupFailuresKey]).IsTypeOf<AggregateException>();
        var cleanup = caught.Data[GitRepositoryWorkspace.CleanupFailuresKey] as AggregateException;
        await Assert.That(cleanup!.InnerExceptions.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(cleanup.InnerExceptions[0], cleanupFailure)).IsTrue();
    }
}
