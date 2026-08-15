using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Emission;
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
                Request([GenerationTestData.Source("../escape.cs", "unsafe")]),
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
                Request([]),
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
                Request([GenerationTestData.Source("Models/Escape.cs", "unsafe")]),
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
                Request([GenerationTestData.Source("Models/widget.cs", "new")]),
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
            new FileDescription(stalePath, GenerationTestData.OwnedContent("stale")),
            new FileDescription(unrelatedPath, "manual"));
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var result = await writer.WriteAsync(
            Request([GenerationTestData.Source("Models/Fresh.cs", "fresh")]),
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

        _ = await writer.WriteAsync(Request(sources), CancellationToken.None);
        var verify = await writer.WriteAsync(Request(sources, verify: true), CancellationToken.None);

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
        _ = await writer.WriteAsync(Request(sources), CancellationToken.None);
        var outputPath = fileSystem.Path.Combine(GenerationTestData.OutputRoot, relativePath);
        await fileSystem.File.WriteAllTextAsync(outputPath, GenerationTestData.OwnedContent("drift\n"), CancellationToken.None);

        var result = await writer.WriteAsync(Request(sources, verify: true), CancellationToken.None);

        await Assert.That(result.HasChanges).IsTrue();
        await Assert.That(result.ChangedPaths).Contains(relativePath);
        var repaired = await fileSystem.File.ReadAllTextAsync(outputPath, CancellationToken.None);
        await Assert.That(repaired).IsEqualTo(GenerationTestData.OwnedContent("expected\n"));
    }

    [Test]
    public async Task WriteAsync_Should_Admit_Root_And_Adapter_Sources()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var result = await writer.WriteAsync(
            Request(
            [
                GenerationTestData.Source("ExampleClient.cs", "client"),
                GenerationTestData.Source("Internal/ResponseAdapters/ExampleAdapter.cs", "adapter"),
            ]),
            CancellationToken.None);

        await Assert.That(result.CreatedPaths).Contains("ExampleClient.cs");
        await Assert.That(result.CreatedPaths).Contains("Internal/ResponseAdapters/ExampleAdapter.cs");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_A_Family_Folder_Shadowing_By_Case()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert
            .That(async () => await writer.WriteAsync(Request([], familyFolders: ["models"]), CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("shadows");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_Family_Folders_Duplicated_By_Case()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert
            .That(async () => await writer.WriteAsync(
                Request([], familyFolders: ["Sessions", "sessions"]),
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("duplicated");
    }

    [Test]
    public async Task WriteAsync_Should_Admit_Family_Folder_Sources()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var result = await writer.WriteAsync(
            Request(
                [GenerationTestData.Source("Sessions/SessionsClient.cs", "client")],
                familyFolders: ["Sessions"]),
            CancellationToken.None);

        await Assert.That(result.CreatedPaths).Contains("Sessions/SessionsClient.cs");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_A_Source_Outside_The_Admitted_Family_Folders()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert.That(async () => await writer.WriteAsync(
                Request(
                    [GenerationTestData.Source("Widgets/WidgetsClient.cs", "client")],
                    familyFolders: ["Sessions"]),
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("outside the owned source directories");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_A_Nested_Family_Folder_Source()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert.That(async () => await writer.WriteAsync(
                Request(
                    [GenerationTestData.Source("Sessions/Deep/File.cs", "nested")],
                    familyFolders: ["Sessions"]),
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("outside the owned source directories");
    }

    [Test]
    public async Task WriteAsync_Should_Delete_Stale_Sources_Of_A_Retired_Family_Folder()
    {
        const string stalePath = "src/OpenCode.Sdk/Widgets/Old.cs";
        var fileSystem = GenerationTestData.CreateFileSystem();
        var manifest = JsonSerializer.Serialize(new GenerationManifest { Files = ["Widgets/Old.cs"], });
        fileSystem.Initialize().With(
            new FileDescription(GenerationTestData.ManifestPath, manifest),
            new FileDescription(stalePath, GenerationTestData.OwnedContent("stale")));
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var result = await writer.WriteAsync(
            Request([], familyFolders: ["Sessions"]),
            CancellationToken.None);

        await Assert.That(fileSystem.File.Exists(stalePath)).IsFalse();
        await Assert.That(result.DeletedPaths).Contains("Widgets/Old.cs");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_A_Family_Folder_Shadowing_A_Static_Owned_Directory()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert.That(async () => await writer.WriteAsync(
                Request([], familyFolders: ["Models"]),
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("Models");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_Overwriting_An_Unmanifested_File()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        fileSystem.Initialize().With(new FileDescription("src/OpenCode.Sdk/ExampleClient.cs", "hand-written"));
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert
            .That(async () => _ = await writer.WriteAsync(
                Request([GenerationTestData.Source("ExampleClient.cs", "generated")]),
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("unmanifested");
        var untouched = await fileSystem.File.ReadAllTextAsync("src/OpenCode.Sdk/ExampleClient.cs", CancellationToken.None);
        await Assert.That(untouched).IsEqualTo("hand-written");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_Overwriting_A_Manifest_Entry_Without_Provenance()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var manifest = JsonSerializer.Serialize(new GenerationManifest { Files = ["Models/Example.cs"], });
        fileSystem.Initialize().With(
            new FileDescription(GenerationTestData.ManifestPath, manifest),
            new FileDescription("src/OpenCode.Sdk/Models/Example.cs", "hand-edited without a header"));
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert
            .That(async () => _ = await writer.WriteAsync(
                Request([GenerationTestData.Source("Models/Example.cs", "generated")]),
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("provenance");
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_Deleting_A_Manifest_Entry_Without_Provenance()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var manifest = JsonSerializer.Serialize(new GenerationManifest { Files = ["Models/Stale.cs"], });
        fileSystem.Initialize().With(
            new FileDescription(GenerationTestData.ManifestPath, manifest),
            new FileDescription("src/OpenCode.Sdk/Models/Stale.cs", "hand-edited without a header"));
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        var exception = await Assert
            .That(async () => _ = await writer.WriteAsync(
                Request([GenerationTestData.Source("Models/Fresh.cs", "generated")]),
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("provenance");
        await Assert.That(fileSystem.File.Exists("src/OpenCode.Sdk/Models/Stale.cs")).IsTrue();
    }

    [Test]
    public async Task WriteAsync_Should_Refuse_Headerless_Generated_Sources()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));
        var headerless = new GeneratedSource
        {
            RelativePath = "Models/Example.cs",
            Utf8Source = "no header"u8.ToArray(),
        };

        var exception = await Assert
            .That(async () => _ = await writer.WriteAsync(
                Request([headerless]),
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception?.Message).Contains("provenance header");
    }

    [Test]
    public async Task WriteAsync_Should_Create_And_Remove_The_Partial_Marker()
    {
        var fileSystem = GenerationTestData.CreateFileSystem();
        var writer = new GenerationWriter(fileSystem, new RecordingProjectFormatter(fileSystem));

        _ = await writer.WriteAsync(
            Request([], partialMarkerContent: "Pending operations: 3.\n"),
            CancellationToken.None);
        var removal = await writer.WriteAsync(Request([]), CancellationToken.None);

        await Assert.That(fileSystem.File.Exists(GenerationTestData.MarkerPath)).IsFalse();
        await Assert.That(removal.DeletedPaths).Contains(".generation-incomplete");
    }

    /// <summary>Assembles one write request over the shared test roots; flags default off.</summary>
    private static GenerationWriteRequest Request(IReadOnlyList<GeneratedSource> sources,
        IReadOnlyList<string>? familyFolders = null, string? partialMarkerContent = null, bool verify = false) =>
        new()
        {
            OutputRoot = GenerationTestData.OutputRoot,
            ProjectPath = GenerationTestData.ProjectPath,
            Sources = sources,
            FamilyFolders = familyFolders ?? [],
            PartialMarkerContent = partialMarkerContent,
            Verify = verify,
        };
}
