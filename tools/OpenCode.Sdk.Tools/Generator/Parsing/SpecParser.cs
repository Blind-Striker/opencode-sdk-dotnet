using System.IO.Abstractions;
using System.Text.Json;

namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>Parses the pinned OpenAPI 3.1 document into the wire-faithful SpecIR.</summary>
public sealed class SpecParser
{
    private readonly IFileSystem _fileSystem;

    /// <summary>Creates the parser over the injected filesystem (TestableIO seam).</summary>
    public SpecParser(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <summary>Parses the spec file; refuses unknown constructs with batched errors.</summary>
    public SpecDocument Parse(string specPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specPath);

        if (!_fileSystem.File.Exists(specPath))
        {
            throw new SpecParseException([$"document: spec file '{specPath}' does not exist",]);
        }

        var text = _fileSystem.File.ReadAllText(specPath);
        using var json = ParseJson(text);
        return ParseDocument(json.RootElement);
    }

    private static JsonDocument ParseJson(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new SpecParseException($"document: not valid JSON — {exception.Message}", exception);
        }
    }

    private static SpecDocument ParseDocument(JsonElement root)
    {
        SpecParseErrorCollector errors = new();
        var version = ReadVersion(root, errors);
        // Document-key wall: openapi, info, paths, components, security, tags.
        foreach (var property in root
                     .EnumerateObject()
                     .Where(static property =>
                         property.Name is not ("openapi" or "info" or "paths" or "components" or "security" or "tags")))
        {
            errors.Add("document", $"unknown top-level key '{property.Name}'");
        }

        var schemas = ReadSchemas(root, errors);
        errors.ThrowIfAny();

        return new SpecDocument
        {
            OpenApiVersion = version,
            Operations = [],
            Schemas = schemas,
        };
    }

    private static string ReadVersion(JsonElement root, SpecParseErrorCollector errors)
    {
        if (!root.TryGetProperty("openapi", out var version) || version.ValueKind is not JsonValueKind.String)
        {
            errors.Add("document", "missing 'openapi' version string");
            return string.Empty;
        }

        var text = version.GetString() ?? string.Empty;
        if (!text.StartsWith("3.1.", StringComparison.Ordinal))
        {
            errors.Add("document", $"unsupported OpenAPI version '{text}' — the dialect wall accepts 3.1.x only");
        }

        return text;
    }

    private static SortedDictionary<string, SchemaNode> ReadSchemas(JsonElement root, SpecParseErrorCollector errors)
    {
        SortedDictionary<string, SchemaNode> graph = new(StringComparer.Ordinal);
        if (!root.TryGetProperty("components", out var components))
        {
            return graph;
        }

        foreach (var member in components
                     .EnumerateObject()
                     .Where(static member => !string.Equals(member.Name, "schemas", StringComparison.Ordinal)))
        {
            errors.Add("document", $"unknown components member '{member.Name}'");
        }

        return graph;
    }
}
