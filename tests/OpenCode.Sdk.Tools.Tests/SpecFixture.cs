using System.IO.Abstractions.TestingHelpers;
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

internal static class SpecFixture
{
    public const string SpecPath = "spec/openapi.json";

    public static SpecDocument Parse(string documentJson)
    {
        MockFileSystem fileSystem = new();
        fileSystem.AddFile(SpecPath, new MockFileData(documentJson));
        return new SpecParser(fileSystem).Parse(SpecPath);
    }

    public static SpecDocument ParseSchemas(string schemasJson) =>
        Parse($$"""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {{schemasJson}} }
        }
        """);

    public static SpecDocument ParsePaths(string pathsJson, string schemasJson = "{}") =>
        Parse($$"""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {{pathsJson}},
          "components": { "schemas": {{schemasJson}} }
        }
        """);
}
