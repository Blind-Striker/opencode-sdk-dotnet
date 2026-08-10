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
        var operations = ReadOperations(root, errors);
        ValidateDanglingRefs(schemas, errors);
        errors.ThrowIfAny();

        return new SpecDocument
        {
            OpenApiVersion = version,
            Operations = operations,
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

    private static List<SpecOperation> ReadOperations(JsonElement root, SpecParseErrorCollector errors)
    {
        List<SpecOperation> operations = [];
        HashSet<string> operationIds = new(StringComparer.Ordinal);
        if (!root.TryGetProperty("paths", out var paths))
        {
            return operations;
        }

        if (paths.ValueKind is not JsonValueKind.Object)
        {
            errors.Add("document", "'paths' must be an object");
            return operations;
        }

        foreach (var pathItem in paths.EnumerateObject())
        {
            ReadPathItem(pathItem, operations, operationIds, errors);
        }

        return operations;
    }

    private static void ReadPathItem(JsonProperty pathItem,
        List<SpecOperation> operations,
        HashSet<string> operationIds,
        SpecParseErrorCollector errors)
    {
        var pathLocation = $"path '{pathItem.Name}'";
        var hasWildcardPath = ReadWildcardPath(pathItem.Name, pathLocation, errors);
        if (pathItem.Value.ValueKind is not JsonValueKind.Object)
        {
            errors.Add(pathLocation, "path item must be an object");
            return;
        }

        foreach (var method in pathItem.Value.EnumerateObject())
        {
            if (!IsSupportedMethod(method.Name))
            {
                errors.Add(pathLocation, $"unknown path-item key '{method.Name}'");
                continue;
            }

            var operation = ReadOperation(method.Value, pathItem.Name, method.Name, hasWildcardPath, operationIds, errors);
            if (operation is not null)
            {
                operations.Add(operation);
            }
        }
    }

    private static bool IsSupportedMethod(string method) => method is "get" or "put" or "post" or "delete" or "patch";

    private static bool ReadWildcardPath(string path, string location, SpecParseErrorCollector errors)
    {
        var wildcardIndex = path.IndexOf('*', StringComparison.Ordinal);
        if (wildcardIndex < 0)
        {
            return false;
        }

        if (wildcardIndex == path.Length - 1 && path.EndsWith("/*", StringComparison.Ordinal))
        {
            return true;
        }

        errors.Add(location, "wildcard is only valid as a terminal '/*' path segment");
        return false;
    }

    private static SpecOperation? ReadOperation(JsonElement operation,
        string path,
        string method,
        bool hasWildcardPath,
        HashSet<string> operationIds,
        SpecParseErrorCollector errors)
    {
        var pathMethodLocation = $"path '{path}' method '{method}'";
        if (operation.ValueKind is not JsonValueKind.Object)
        {
            errors.Add(pathMethodLocation, "operation must be an object");
            return null;
        }

        var operationId = ReadOperationId(operation, pathMethodLocation, errors);
        var location = operationId is null ? pathMethodLocation : $"operation '{operationId}'";
        if (RefuseUnsupportedOperationKeys(operation, location, errors)
            || operationId is null
            || !TryReadOperationIdentity(operationId, location, errors, out var surface, out var segments))
        {
            return null;
        }

        if (!operationIds.Add(operationId))
        {
            errors.Add(location, $"duplicate operationId '{operationId}'");
        }

        if (!TryReadOptionalString(operation, "summary", location, errors, out var summary)
            || !TryReadOptionalString(operation, "description", location, errors, out var description)
            || !TryReadDeprecated(operation, location, errors, out var isDeprecated)
            || !TryReadWebSocket(operation, location, errors, out var isWebSocket))
        {
            return null;
        }

        return new SpecOperation
        {
            OperationId = operationId,
            Surface = surface,
            Segments = segments,
            Method = method,
            Path = path,
            HasWildcardPath = hasWildcardPath,
            IsWebSocket = isWebSocket,
            IsDeprecated = isDeprecated,
            Summary = summary,
            Description = description,
        };
    }

    private static string? ReadOperationId(JsonElement operation, string location, SpecParseErrorCollector errors)
    {
        if (!operation.TryGetProperty("operationId", out var operationIdElement)
            || operationIdElement.ValueKind is not JsonValueKind.String)
        {
            errors.Add(location, "operationId must be a non-empty string");
            return null;
        }

        var operationId = operationIdElement.GetString();
        if (string.IsNullOrWhiteSpace(operationId))
        {
            errors.Add(location, "operationId must be a non-empty string");
            return null;
        }

        return operationId;
    }

    private static bool RefuseUnsupportedOperationKeys(JsonElement operation, string location, SpecParseErrorCollector errors)
    {
        var refused = false;
        foreach (var propertyName in operation
                     .EnumerateObject()
                     .Select(static property => property.Name)
                     .Where(static propertyName => !IsSupportedOperationKey(propertyName)))
        {
            errors.Add(location, $"unknown operation key '{propertyName}'");
            refused = true;
        }

        return refused;
    }

    private static bool IsSupportedOperationKey(string name)
    {
        // Parameters, request bodies, and responses are accepted by the wall; not parsed into the IR.
        return name is "operationId" or "summary" or "description" or "tags" or "security" or "deprecated" or "parameters"
            or "requestBody" or "responses" or "x-codeSamples" or "x-websocket";
    }

    private static bool TryReadOperationIdentity(string operationId,
        string location,
        SpecParseErrorCollector errors,
        out SpecSurface surface,
        out IReadOnlyList<string> segments)
    {
        var parsedSegments = operationId.Split('.');
        if (string.Equals(parsedSegments[0], "v2", StringComparison.Ordinal))
        {
            surface = SpecSurface.Modern;
            segments = parsedSegments[1..];
        }
        else
        {
            surface = SpecSurface.Legacy;
            segments = parsedSegments;
        }

        if (segments.Count > 0)
        {
            return true;
        }

        errors.Add(location, "operationId must have at least one segment after 'v2'");
        return false;
    }

    private static bool TryReadOptionalString(JsonElement operation,
        string name,
        string location,
        SpecParseErrorCollector errors,
        out string? value)
    {
        value = null;
        if (!operation.TryGetProperty(name, out var property))
        {
            return true;
        }

        if (property.ValueKind is not JsonValueKind.String)
        {
            errors.Add(location, $"{name} must be a string");
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryReadDeprecated(JsonElement operation, string location, SpecParseErrorCollector errors, out bool isDeprecated)
    {
        isDeprecated = false;
        if (!operation.TryGetProperty("deprecated", out var deprecated))
        {
            return true;
        }

        if (deprecated.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add(location, "deprecated must be a boolean");
            return false;
        }

        isDeprecated = deprecated.GetBoolean();
        return true;
    }

    private static bool TryReadWebSocket(JsonElement operation, string location, SpecParseErrorCollector errors, out bool isWebSocket)
    {
        isWebSocket = false;
        if (!operation.TryGetProperty("x-websocket", out var webSocket))
        {
            return true;
        }

        if (webSocket.ValueKind is not JsonValueKind.True)
        {
            errors.Add(location, "x-websocket must be literal true");
            return false;
        }

        isWebSocket = true;
        return true;
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

        if (!components.TryGetProperty("schemas", out var schemas))
        {
            return graph;
        }

        SchemaNodeParser parser = new(errors, graph);
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

        return graph;
    }

    private static void ValidateDanglingRefs(IReadOnlyDictionary<string, SchemaNode> graph, SpecParseErrorCollector errors)
    {
        foreach (var (root, node) in graph)
        {
            ValidateDanglingRefs(root, node, graph, errors);
        }
    }

    private static void ValidateDanglingRefs(string root, SchemaNode node, IReadOnlyDictionary<string, SchemaNode> graph,
        SpecParseErrorCollector errors)
    {
        if (node is RefNode reference && !graph.ContainsKey(reference.Target))
        {
            errors.Add($"schema '{root}'", $"unresolved ref '{reference.Target}'");
        }

        foreach (var child in node.Children)
        {
            ValidateDanglingRefs(root, child, graph, errors);
        }
    }
}
