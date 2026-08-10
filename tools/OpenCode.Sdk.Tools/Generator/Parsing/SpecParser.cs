using System.Collections.ObjectModel;
using System.IO.Abstractions;
using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Parsing.Operations;
using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

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
        if (root.ValueKind is not JsonValueKind.Object)
        {
            errors.Add("document", "root must be an object");
            errors.ThrowIfAny();
        }

        var version = ReadVersion(root, errors);
        // Document-key wall: openapi, info, paths, components, security, tags.
        foreach (var property in root
                     .EnumerateObject()
                     .Where(static property =>
                         property.Name is not ("openapi" or "info" or "paths" or "components" or "security" or "tags")))
        {
            errors.Add("document", $"unknown top-level key '{property.Name}'");
        }

        SortedDictionary<string, SchemaNode> schemas = new(StringComparer.Ordinal);
        SchemaNodeParser schemaParser = new(errors, schemas);
        ReadSchemas(root, schemaParser, schemas, errors);
        OperationParser operationParser = new(schemaParser, errors);
        var operations = operationParser.Parse(root);
        ValidateDanglingRefs(schemas, operationParser.SchemaRoots, errors);
        errors.ThrowIfAny();

        var frozenSchemas = new ReadOnlyDictionary<string, SchemaNode>(
            new SortedDictionary<string, SchemaNode>(schemas, StringComparer.Ordinal));
        return new SpecDocument
        {
            OpenApiVersion = version,
            Operations = operations,
            Schemas = frozenSchemas,
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

    private static void ReadSchemas(JsonElement root,
        SchemaNodeParser parser,
        SortedDictionary<string, SchemaNode> graph,
        SpecParseErrorCollector errors)
    {
        if (!root.TryGetProperty("components", out var components))
        {
            return;
        }

        if (components.ValueKind is not JsonValueKind.Object)
        {
            errors.Add("document", "'components' must be an object");
            return;
        }

        foreach (var member in components
                     .EnumerateObject()
                     .Where(static member => !string.Equals(member.Name, "schemas", StringComparison.Ordinal)))
        {
            errors.Add("document", $"unknown components member '{member.Name}'");
        }

        if (!components.TryGetProperty("schemas", out var schemas))
        {
            return;
        }

        if (schemas.ValueKind is not JsonValueKind.Object)
        {
            errors.Add("document", "'components.schemas' must be an object");
            return;
        }

        foreach (var schema in schemas.EnumerateObject())
        {
            var node = parser.Parse(schema.Value, schema.Name, string.Empty);
            if (node is null)
            {
                continue;
            }

            if (!graph.TryAdd(schema.Name, node))
            {
                errors.Add($"schema '{schema.Name}'", $"schema graph key collision '{schema.Name}'");
            }
        }
    }

    private static void ValidateDanglingRefs(IReadOnlyDictionary<string, SchemaNode> graph,
        IEnumerable<(string Location, SchemaNode Node)> operationRoots,
        SpecParseErrorCollector errors)
    {
        foreach (var (root, node) in graph)
        {
            ValidateNodeDanglingRefs($"schema '{root}'", node, graph, errors);
        }

        foreach (var (location, node) in operationRoots)
        {
            ValidateNodeDanglingRefs(location, node, graph, errors);
        }
    }

    private static void ValidateNodeDanglingRefs(string location,
        SchemaNode node,
        IReadOnlyDictionary<string, SchemaNode> graph,
        SpecParseErrorCollector errors)
    {
        if (node is RefNode reference && !graph.ContainsKey(reference.Target))
        {
            errors.Add(location, $"unresolved ref '{reference.Target}'");
        }

        foreach (var child in node.Children)
        {
            ValidateNodeDanglingRefs(location, child, graph, errors);
        }
    }
}
