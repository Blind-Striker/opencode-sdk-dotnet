using System.Text;
using OpenCode.Sdk.Tools.Generator.Emission;
using Testably.Abstractions.Testing;
using Testably.Abstractions.Testing.Initializer;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal static class GenerationTestData
{
    public const string ManifestPath = "src/OpenCode.Sdk/.generated-manifest.json";
    public const string MarkerPath = "src/OpenCode.Sdk/.generation-incomplete";
    public const string OutputRoot = "src/OpenCode.Sdk";
    public const string ProjectPath = "src/OpenCode.Sdk/OpenCode.Sdk.csproj";

    private const string Curation = """
        {
          "groups": {
            "health": {
              "placement": "root"
            }
          },
          "envelopePayloadNames": {},
          "propertyOverrides": []
        }
        """;

    public static MockFileSystem CreateFileSystem()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(ProjectPath, "<Project />"));
        return fileSystem;
    }

    public static MockFileSystem CreateCommandFileSystem()
    {
        var spec = new SpecDocumentBuilder()
            .WithOperation("v2.health.get", path: "/api/health")
            .WithOperation("session.list", path: "/session")
            .BuildJson();
        var fileSystem = CreateFileSystem();
        fileSystem.Initialize().With(
            new FileDescription("spec/openapi.json", spec),
            new FileDescription("tools/generation-profile.txt", "v2.health.get\n"),
            new FileDescription("tools/curation.json", Curation));
        return fileSystem;
    }

    public static GeneratedSource Source(string relativePath, string source) =>
        new()
        {
            RelativePath = relativePath,
            Utf8Source = Encoding.UTF8.GetBytes(source),
        };
}
