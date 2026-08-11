using System.Text.Json;
using OpenCode.Sdk.Tools.Output;
using OpenCode.Sdk.Tools.Tests.Support;
using Testably.Abstractions.Testing;
using Testably.Abstractions.Testing.Initializer;

namespace OpenCode.Sdk.Tools.Tests.Output;

public sealed class GenerationWriterTests
{
    [Test]
    public async Task WriteAsync_Should_Refuse_Unsafe_Source_Paths()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert.That(async () => await writer.WriteAsync(
                GenerationTestData.OutputRoot,
                GenerationTestData.ProjectPath,
                [GenerationTestData.Source("../escape.cs", "unsafe")],
                partialMarkerContent: null,
                verify: false,
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("unsafe");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_Unsafe_Manifest_Paths()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        fileSystem.Initialize().With(new FileDescription(
            GenerationTestData.ManifestPath,
            "{\"files\":[\"../outside.cs\"]}"));
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert.That(async () => await writer.WriteAsync(
                GenerationTestData.OutputRoot,
                GenerationTestData.ProjectPath,
                [],
                partialMarkerContent: null,
                verify: false,
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("unsafe");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_Symbolic_Links_In_Owned_Paths()
    {
        const string outsideRoot = "outside";
        var fileSystem = GenerationTestData.CreateFileSystem();
        fileSystem.Directory.CreateDirectory(outsideRoot);
        _ = fileSystem.Directory.CreateSymbolicLink(
            fileSystem.Path.Combine(GenerationTestData.OutputRoot, "Models"),
            outsideRoot);
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert.That(async () => await writer.WriteAsync(
                GenerationTestData.OutputRoot,
                GenerationTestData.ProjectPath,
                [GenerationTestData.Source("Models/Escape.cs", "unsafe")],
                partialMarkerContent: null,
                verify: false,
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("symbolic link");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_Case_Only_Owned_Path_Changes()
    {
        const string oldPath = "Models/Widget.cs";
        var fileSystem = GenerationTestData.CreateFileSystem();
        var manifest = JsonSerializer.Serialize(new GenerationManifest { Files = [oldPath], });
        fileSystem.Initialize().With(
            new FileDescription(GenerationTestData.ManifestPath, manifest),
            new FileDescription(fileSystem.Path.Combine(GenerationTestData.OutputRoot, oldPath), "old"));
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert.That(async () => await writer.WriteAsync(
                GenerationTestData.OutputRoot,
                GenerationTestData.ProjectPath,
                [GenerationTestData.Source("Models/widget.cs", "new")],
                partialMarkerContent: null,
                verify: false,
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("only by case");
    }

    [Test]
    public async Task WriteAsync_Should_Delete_Only_Stale_Manifest_Files()
    {
        const string stalePath = "src/OpenCode.Sdk/Models/Stale.cs";
        const string unrelatedPath = "src/OpenCode.Sdk/Manual.cs";
        var fileSystem = GenerationTestData.CreateFileSystem();
        var manifest = JsonSerializer.Serialize(new GenerationManifest { Files = ["Models/Stale.cs"], });
        fileSystem.Initialize().With(
            new FileDescription(GenerationTestData.ManifestPath, manifest),
            new FileDescription(stalePath, "stale"),
            new FileDescription(unrelatedPath, "manual"));
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var result = await writer.WriteAsync(
            GenerationTestData.OutputRoot,
            GenerationTestData.ProjectPath,
            [GenerationTestData.Source("Models/Fresh.cs", "fresh")],
            partialMarkerContent: null,
            verify: false,
            CancellationToken.None);

        await Assert.That(fileSystem.File.Exists(stalePath)).IsFalse();
        await Assert.That(fileSystem.File.Exists(unrelatedPath)).IsTrue();
        await Assert.That(result.DeletedPaths).Contains("Models/Stale.cs");
        await Assert.That(result.CreatedPaths).Contains("Models/Fresh.cs");
    }

    [Test]
    public async Task WriteAsync_Should_Be_Idempotent_After_Project_Formatting()
    {
        const string relativePath = "Models/Example.cs";
        var fileSystem = GenerationTestData.CreateFileSystem();
        var formatter = new RecordingProjectFormatter(fileSystem, async (currentFileSystem, _, _, cancellationToken) =>
        {
            var path = currentFileSystem.Path.Combine(GenerationTestData.OutputRoot, relativePath);
            var source = await currentFileSystem.File.ReadAllTextAsync(path, cancellationToken);
            await currentFileSystem.File.WriteAllTextAsync(
                path,
                source.Replace("public record", "public sealed record", StringComparison.Ordinal),
                cancellationToken);
        });
        var writer = new GenerationWriter(fileSystem, formatter);
        var sources = new[] { GenerationTestData.Source(relativePath, "public record Example;\n"), };

        _ = await writer.WriteAsync(
            GenerationTestData.OutputRoot,
            GenerationTestData.ProjectPath,
            sources,
            partialMarkerContent: null,
            verify: false,
            CancellationToken.None);
        var verify = await writer.WriteAsync(
            GenerationTestData.OutputRoot,
            GenerationTestData.ProjectPath,
            sources,
            partialMarkerContent: null,
            verify: true,
            CancellationToken.None);

        await Assert.That(formatter.CallCount).IsEqualTo(2);
        await Assert.That(formatter.SourcePaths).Contains(relativePath);
        await Assert.That(verify.HasChanges).IsFalse();
    }

    [Test]
    public async Task WriteAsync_Should_Report_And_Repair_Verify_Drift()
    {
        const string relativePath = "Models/Example.cs";
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));
        var sources = new[] { GenerationTestData.Source(relativePath, "expected\n"), };
        _ = await writer.WriteAsync(
            GenerationTestData.OutputRoot,
            GenerationTestData.ProjectPath,
            sources,
            partialMarkerContent: null,
            verify: false,
            CancellationToken.None);
        var outputPath = fileSystem.Path.Combine(GenerationTestData.OutputRoot, relativePath);
        await fileSystem.File.WriteAllTextAsync(outputPath, "drift\n", CancellationToken.None);

        var result = await writer.WriteAsync(
            GenerationTestData.OutputRoot,
            GenerationTestData.ProjectPath,
            sources,
            partialMarkerContent: null,
            verify: true,
            CancellationToken.None);

        await Assert.That(result.HasChanges).IsTrue();
        await Assert.That(result.ChangedPaths).Contains(relativePath);
        var repaired = await fileSystem.File.ReadAllTextAsync(outputPath, CancellationToken.None);
        await Assert.That(repaired).IsEqualTo("expected\n");
    }

    [Test]
    public async Task WriteAsync_Should_Create_And_Remove_The_Partial_Marker()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        _ = await writer.WriteAsync(
            GenerationTestData.OutputRoot,
            GenerationTestData.ProjectPath,
            [],
            "Pending modern operations: 1.\nPending legacy operations: 2.\n",
            verify: false,
            CancellationToken.None);
        var removal = await writer.WriteAsync(
            GenerationTestData.OutputRoot,
            GenerationTestData.ProjectPath,
            [],
            partialMarkerContent: null,
            verify: false,
            CancellationToken.None);

        await Assert.That(fileSystem.File.Exists(GenerationTestData.MarkerPath)).IsFalse();
        await Assert.That(removal.DeletedPaths).Contains(".generation-incomplete");
    }
}
