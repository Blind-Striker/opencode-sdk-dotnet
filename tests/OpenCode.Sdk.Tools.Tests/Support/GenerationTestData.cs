using System.Text;
using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Serialization;
using Testably.Abstractions.Testing;
using Testably.Abstractions.Testing.Initializer;
using static OpenCode.Sdk.Tools.Tests.Support.BindingScenarioData;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal static class GenerationTestData
{
    public const string ManifestPath = "src/OpenCode.Sdk/.generated-manifest.json";
    public const string MarkerPath = "src/OpenCode.Sdk/.generation-incomplete";
    public const string OutputRoot = "src/OpenCode.Sdk";
    public const string ProjectPath = "src/OpenCode.Sdk/OpenCode.Sdk.csproj";

    /// <summary>The WebSocket operation the transport-owned command roots pin for a hand-written door.</summary>
    public const string TransportOwnedOperationId = "v2.pty.connect";

    private const string SpecPath = "spec/openapi.json";
    private const string ProfilePath = "tools/generation-profile.txt";
    private const string CurationPath = "tools/curation.json";

    /// <summary>Provenance-headed content for a generated file that already sits on disk.</summary>
    public static string OwnedContent(string content) => $"{GenerationProvenance.Header}{content}";

    public static MockFileSystem CreateFileSystem()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(ProjectPath, "<Project />"));
        return fileSystem;
    }

    /// <summary>The generate command's repository roots: one selected operation, two pending, no transport-owned row.</summary>
    public static MockFileSystem CreateCommandFileSystem()
    {
        var fileSystem = CreateCommandRoots(extend: null);
        WriteCuration(fileSystem, transportOwned: []);
        return fileSystem;
    }

    /// <summary>
    /// The command roots plus a WebSocket operation covered by a <c>transportOwned</c> row whose
    /// fingerprint is computed from the scenario's own ingested shape, the way the committed row is.
    /// </summary>
    public static async Task<MockFileSystem> CreateTransportOwnedCommandFileSystemAsync(CancellationToken cancellationToken = default)
    {
        var fileSystem = CreateCommandRoots(spec => spec
            .WithOperation(TransportOwnedOperationId, path: "/api/pty/{ptyID}/connect", configure: operation => operation
                .Parameter("ptyID", "path", schema => schema.Type("string"), required: true)
                .Extension("x-websocket", "true")));
        var document = await new SpecIngestion(fileSystem).IngestAsync(SpecPath, cancellationToken);
        var operation = document.Operations.Single(static candidate => candidate.OperationId == TransportOwnedOperationId);
        var row = TransportOwned(TransportOwnedOperationId, TransportOwnedFingerprint.ComputeSha256(operation));
        WriteCuration(fileSystem, transportOwned: [row]);
        return fileSystem;
    }

    public static GeneratedSource Source(string relativePath, string source) =>
        new()
        {
            RelativePath = relativePath,
            Utf8Source = Encoding.UTF8.GetBytes(OwnedContent(source)),
        };

    private static MockFileSystem CreateCommandRoots(Action<SpecDocumentBuilder>? extend)
    {
        var spec = new SpecDocumentBuilder()
            .WithSchema("ExampleHealth", schema => schema
                .Type("object")
                .Property("healthy", property => property.Type("boolean"), required: true))
            .WithSchema("ExamplePlugin", schema => schema
                .Type("object")
                .Property("name", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", path: "/api/health", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ExampleHealth")))
            .WithOperation("v2.plugin.list", path: "/api/plugin", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ExamplePlugin")))
            .WithOperation("v2.session.list", path: "/api/session")
            // Two independent wire-shape walls (wildcard path, WebSocket) on the same operation,
            // so the pending map proves the telltale lists every wall, not only the first.
            .WithOperation("v2.widget.tail", path: "/api/widget/*", configure: operation => operation
                .Extension("x-websocket", "true"));
        extend?.Invoke(spec);
        var fileSystem = CreateFileSystem();
        fileSystem
            .Initialize()
            .With(
                new FileDescription(SpecPath, spec.BuildJson()),
                new FileDescription(ProfilePath, "v2.health.get\n"));
        return fileSystem;
    }

    /// <summary>Writes the command curation as the loader reads it: the root-placed health group plus the given transport-owned rows.</summary>
    private static void WriteCuration(MockFileSystem fileSystem, IReadOnlyList<TransportOwnedCuration> transportOwned)
    {
        var curation = Curation(Groups("health", RootGroup()), transportOwned: transportOwned);
        fileSystem
            .Initialize()
            .With(new FileDescription(CurationPath, JsonSerializer.Serialize(curation, ToolJsonContext.Default.GenerationCuration)));
    }
}
