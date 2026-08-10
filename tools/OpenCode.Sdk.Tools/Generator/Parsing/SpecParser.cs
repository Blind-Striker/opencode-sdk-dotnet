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

        SortedDictionary<string, SchemaNode> schemas = new(StringComparer.Ordinal);
        SchemaNodeParser schemaParser = new(errors, schemas);
        ReadSchemas(root, schemaParser, schemas, errors);
        var operations = ReadOperations(root, schemaParser, errors);
        ValidateDanglingRefs(schemas, operations, errors);
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

    private static List<SpecOperation> ReadOperations(JsonElement root,
        SchemaNodeParser schemaParser,
        SpecParseErrorCollector errors)
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
            ReadPathItem(pathItem, operations, operationIds, schemaParser, errors);
        }

        return operations;
    }

    private static void ReadPathItem(JsonProperty pathItem,
        List<SpecOperation> operations,
        HashSet<string> operationIds,
        SchemaNodeParser schemaParser,
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

            var operation = ReadOperation(method.Value,
                pathItem.Name,
                method.Name,
                hasWildcardPath,
                operationIds,
                schemaParser,
                errors);
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
        SchemaNodeParser schemaParser,
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

        var hasValidSummary = TryReadOptionalString(operation, "summary", location, errors, out var summary);
        var hasValidDescription = TryReadOptionalString(operation, "description", location, errors, out var description);
        var hasValidDeprecated = TryReadDeprecated(operation, location, errors, out var isDeprecated);
        var hasValidWebSocket = TryReadWebSocket(operation, location, errors, out var isWebSocket);
        var hasValidParameters = TryReadParameters(operation,
            path,
            operationId,
            location,
            schemaParser,
            errors,
            out var parameters);
        var hasValidRequestBody = TryReadRequestBody(operation,
            operationId,
            location,
            schemaParser,
            errors,
            out var requestBody);
        if (!hasValidSummary
            || !hasValidDescription
            || !hasValidDeprecated
            || !hasValidWebSocket
            || !hasValidParameters
            || !hasValidRequestBody)
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
            Parameters = parameters,
            RequestBody = requestBody,
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
        // Responses are accepted by the wall but remain deferred until their operation-side slice.
        return name is "operationId" or "summary" or "description" or "tags" or "security" or "deprecated" or "parameters"
            or "requestBody" or "responses" or "x-codeSamples" or "x-websocket";
    }

    private static bool TryReadParameters(JsonElement operation,
        string path,
        string operationId,
        string location,
        SchemaNodeParser schemaParser,
        SpecParseErrorCollector errors,
        out IReadOnlyList<SpecParameter> parameters)
    {
        List<SpecParameter> parsedParameters = [];
        parameters = parsedParameters;
        if (!operation.TryGetProperty("parameters", out var parameterElements))
        {
            return ValidatePathParameters(path, parsedParameters, location, errors);
        }

        if (parameterElements.ValueKind is not JsonValueKind.Array)
        {
            errors.Add(location, "parameters must be an array");
            return false;
        }

        HashSet<string> parameterKeys = new(StringComparer.Ordinal);
        var valid = true;
        foreach (var parameterElement in parameterElements.EnumerateArray())
        {
            if (TryReadParameter(parameterElement,
                    operationId,
                    location,
                    parameterKeys,
                    schemaParser,
                    errors,
                    out var parameter))
            {
                parsedParameters.Add(parameter!);
            }
            else
            {
                valid = false;
            }
        }

        return ValidatePathParameters(path, parsedParameters, location, errors) && valid;
    }

    private static bool TryReadParameter(JsonElement element,
        string operationId,
        string operationLocation,
        HashSet<string> parameterKeys,
        SchemaNodeParser schemaParser,
        SpecParseErrorCollector errors,
        out SpecParameter? parameter)
    {
        parameter = null;
        if (element.ValueKind is not JsonValueKind.Object)
        {
            errors.Add(operationLocation, "parameter must be an object");
            return false;
        }

        var hasValidName = TryReadParameterName(element, operationLocation, errors, out var name);
        var location = hasValidName ? $"{operationLocation} parameter '{name}'" : $"{operationLocation} parameter";
        var hasKnownKeys = !RefuseUnsupportedParameterKeys(element, location, errors);
        var hasValidLocation = TryReadParameterLocation(element, location, errors, out var parameterLocation);
        if (!hasValidName || !hasValidLocation || !TryAddParameterKey(name, parameterLocation, parameterKeys, location, errors))
        {
            return false;
        }

        var hasValidRequired = TryReadOptionalBoolean(element, "required", location, errors, out var isRequired);
        var hasValidStyle = TryReadDeepObject(element, location, errors, out var isDeepObject);
        var hasValidSchema = TryReadParameterSchema(element,
            operationId,
            name,
            location,
            schemaParser,
            errors,
            out var schema);
        if (!hasKnownKeys || !hasValidRequired || !hasValidStyle || !hasValidSchema)
        {
            return false;
        }

        parameter = new SpecParameter
        {
            Name = name,
            Location = parameterLocation,
            Schema = schema!,
            IsRequired = isRequired,
            IsDeepObject = isDeepObject,
        };
        return true;
    }

    private static bool TryReadParameterName(JsonElement parameter,
        string location,
        SpecParseErrorCollector errors,
        out string name)
    {
        name = string.Empty;
        if (!parameter.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind is not JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            errors.Add(location, "parameter name must be a non-empty string");
            return false;
        }

        name = nameElement.GetString()!;
        return true;
    }

    private static bool RefuseUnsupportedParameterKeys(JsonElement parameter,
        string location,
        SpecParseErrorCollector errors)
    {
        var refused = false;
        foreach (var propertyName in parameter
                     .EnumerateObject()
                     .Select(static property => property.Name)
                     .Where(static propertyName =>
                         propertyName is not ("name" or "in" or "schema" or "required" or "style" or "explode")))
        {
            errors.Add(location, $"unknown parameter key '{propertyName}'");
            refused = true;
        }

        return refused;
    }

    private static bool TryReadParameterLocation(JsonElement parameter,
        string location,
        SpecParseErrorCollector errors,
        out SpecParameterLocation parameterLocation)
    {
        parameterLocation = default;
        if (!parameter.TryGetProperty("in", out var locationElement)
            || locationElement.ValueKind is not JsonValueKind.String)
        {
            errors.Add(location, "parameter location must be a string");
            return false;
        }

        var value = locationElement.GetString();
        switch (value)
        {
            case "path":
                parameterLocation = SpecParameterLocation.Path;
                return true;
            case "query":
                parameterLocation = SpecParameterLocation.Query;
                return true;
            case "header":
                parameterLocation = SpecParameterLocation.Header;
                return true;
            default:
                errors.Add(location, $"unknown parameter location '{value}'");
                return false;
        }
    }

    private static bool TryAddParameterKey(string name,
        SpecParameterLocation parameterLocation,
        HashSet<string> parameterKeys,
        string location,
        SpecParseErrorCollector errors)
    {
        var locationName = parameterLocation switch
        {
            SpecParameterLocation.Path => "path",
            SpecParameterLocation.Query => "query",
            SpecParameterLocation.Header => "header",
            _ => throw new InvalidOperationException("Unknown parameter location."),
        };
        var key = string.Concat(locationName, "\0", name);
        if (parameterKeys.Add(key))
        {
            return true;
        }

        errors.Add(location, $"duplicate parameter '{name}' in '{locationName}'");
        return false;
    }

    private static bool TryReadDeepObject(JsonElement parameter,
        string location,
        SpecParseErrorCollector errors,
        out bool isDeepObject)
    {
        isDeepObject = false;
        var hasStyle = parameter.TryGetProperty("style", out var style);
        var hasExplode = parameter.TryGetProperty("explode", out var explode);
        if (!hasStyle && !hasExplode)
        {
            return true;
        }

        if (!hasStyle || !hasExplode)
        {
            errors.Add(location, "parameter style and explode must be specified together");
            return false;
        }

        if (style.ValueKind is not JsonValueKind.String
            || !string.Equals(style.GetString(), "deepObject", StringComparison.Ordinal))
        {
            errors.Add(location, $"unknown parameter style '{ReadJsonText(style)}'");
            return false;
        }

        if (explode.ValueKind is not JsonValueKind.True)
        {
            errors.Add(location, "deepObject explode must be literal true");
            return false;
        }

        isDeepObject = true;
        return true;
    }

    private static string ReadJsonText(JsonElement value) => value.ValueKind is JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : value.GetRawText();

    private static bool TryReadParameterSchema(JsonElement parameter,
        string operationId,
        string name,
        string location,
        SchemaNodeParser schemaParser,
        SpecParseErrorCollector errors,
        out SchemaNode? schema)
    {
        schema = null;
        if (!parameter.TryGetProperty("schema", out var schemaElement))
        {
            errors.Add(location, "parameter schema is required");
            return false;
        }

        schema = schemaParser.Parse(schemaElement, string.Concat("op:", operationId), $"/parameters/{name}");
        return schema is not null;
    }

    private static bool ValidatePathParameters(string path,
        IReadOnlyList<SpecParameter> parameters,
        string location,
        SpecParseErrorCollector errors)
    {
        var hasValidTemplate = TryReadPathTokens(path, location, errors, out var pathTokens);
        HashSet<string> declaredPathNames = new(
            parameters
                .Where(static parameter => parameter.Location is SpecParameterLocation.Path)
                .Select(static parameter => parameter.Name),
            StringComparer.Ordinal);
        HashSet<string> tokenNames = new(pathTokens, StringComparer.Ordinal);
        var valid = hasValidTemplate;
        foreach (var pathToken in pathTokens.Where(pathToken => !declaredPathNames.Contains(pathToken)))
        {
            errors.Add(location, $"path template token '{{{pathToken}}}' has no declared path parameter");
            valid = false;
        }

        foreach (var parameterName in parameters
                     .Where(static parameter => parameter.Location is SpecParameterLocation.Path)
                     .Select(static parameter => parameter.Name))
        {
            if (tokenNames.Contains(parameterName))
            {
                continue;
            }

            errors.Add(location, $"declared path parameter '{parameterName}' is missing from the path template");
            valid = false;
        }

        return valid;
    }

    private static bool TryReadPathTokens(string path,
        string location,
        SpecParseErrorCollector errors,
        out IReadOnlyList<string> tokens)
    {
        List<string> parsedTokens = [];
        tokens = parsedTokens;
        HashSet<string> seenTokens = new(StringComparer.Ordinal);
        var searchIndex = 0;
        while (true)
        {
            var openOffset = path.AsSpan(searchIndex).IndexOf('{');
            if (openOffset < 0)
            {
                return true;
            }

            var openIndex = searchIndex + openOffset;
            var closeOffset = path.AsSpan(openIndex + 1).IndexOf('}');
            if (closeOffset < 0)
            {
                errors.Add(location, "path template contains an unclosed token");
                return false;
            }

            var closeIndex = openIndex + closeOffset + 1;
            var token = path[(openIndex + 1)..closeIndex];
            if (token.Length == 0)
            {
                errors.Add(location, "path template token must not be empty");
                return false;
            }

            if (seenTokens.Add(token))
            {
                parsedTokens.Add(token);
            }

            searchIndex = closeIndex + 1;
        }
    }

    private static bool TryReadRequestBody(JsonElement operation,
        string operationId,
        string operationLocation,
        SchemaNodeParser schemaParser,
        SpecParseErrorCollector errors,
        out SpecRequestBody? requestBody)
    {
        requestBody = null;
        if (!operation.TryGetProperty("requestBody", out var requestBodyElement))
        {
            return true;
        }

        var location = $"{operationLocation} requestBody";
        if (requestBodyElement.ValueKind is not JsonValueKind.Object)
        {
            errors.Add(location, "requestBody must be an object");
            return false;
        }

        var hasKnownKeys = !RefuseUnsupportedRequestBodyKeys(requestBodyElement, location, errors);
        var hasValidRequired = TryReadOptionalBoolean(requestBodyElement, "required", location, errors, out var isRequired);
        var hasValidContent = TryReadRequestContent(requestBodyElement,
            operationId,
            location,
            schemaParser,
            errors,
            out var contentType,
            out var schema);
        if (!hasKnownKeys || !hasValidRequired || !hasValidContent)
        {
            return false;
        }

        requestBody = new SpecRequestBody
        {
            ContentType = contentType!,
            Schema = schema!,
            IsRequired = isRequired,
        };
        return true;
    }

    private static bool RefuseUnsupportedRequestBodyKeys(JsonElement requestBody,
        string location,
        SpecParseErrorCollector errors)
    {
        var refused = false;
        foreach (var propertyName in requestBody
                     .EnumerateObject()
                     .Select(static property => property.Name)
                     .Where(static propertyName => propertyName is not ("content" or "required")))
        {
            errors.Add(location, $"unknown requestBody key '{propertyName}'");
            refused = true;
        }

        return refused;
    }

    private static bool TryReadRequestContent(JsonElement requestBody,
        string operationId,
        string location,
        SchemaNodeParser schemaParser,
        SpecParseErrorCollector errors,
        out SpecMediaType? contentType,
        out SchemaNode? schema)
    {
        contentType = null;
        schema = null;
        if (!requestBody.TryGetProperty("content", out var content)
            || content.ValueKind is not JsonValueKind.Object)
        {
            errors.Add(location, "requestBody content must contain exactly one media entry");
            return false;
        }

        var mediaEntries = content.EnumerateObject();
        if (!mediaEntries.MoveNext())
        {
            errors.Add(location, "requestBody content must contain exactly one media entry");
            return false;
        }

        var mediaEntry = mediaEntries.Current;
        if (mediaEntries.MoveNext())
        {
            errors.Add(location, "requestBody content must contain exactly one media entry");
            return false;
        }

        var mediaLocation = $"{location} content '{mediaEntry.Name}'";
        var hasValidContentType = TryCreateMediaType(mediaEntry.Name, mediaLocation, errors, out contentType);
        var hasValidMediaObject = TryReadRequestMediaObject(mediaEntry.Value,
            operationId,
            mediaLocation,
            schemaParser,
            errors,
            out schema);
        return hasValidContentType && hasValidMediaObject;
    }

    private static bool TryCreateMediaType(string raw,
        string location,
        SpecParseErrorCollector errors,
        out SpecMediaType? mediaType)
    {
        try
        {
            mediaType = SpecMediaType.Create(raw);
            return true;
        }
        catch (ArgumentException exception)
        {
            errors.Add(location, exception.Message);
            mediaType = null;
            return false;
        }
    }

    private static bool TryReadRequestMediaObject(JsonElement mediaObject,
        string operationId,
        string location,
        SchemaNodeParser schemaParser,
        SpecParseErrorCollector errors,
        out SchemaNode? schema)
    {
        schema = null;
        if (mediaObject.ValueKind is not JsonValueKind.Object)
        {
            errors.Add(location, "media entry must be an object");
            return false;
        }

        var hasKnownKeys = !RefuseUnsupportedRequestMediaKeys(mediaObject, location, errors);
        if (!mediaObject.TryGetProperty("schema", out var schemaElement))
        {
            errors.Add(location, "media entry schema is required");
            return false;
        }

        schema = schemaParser.Parse(schemaElement, string.Concat("op:", operationId), "/requestBody");
        return hasKnownKeys && schema is not null;
    }

    private static bool RefuseUnsupportedRequestMediaKeys(JsonElement mediaObject,
        string location,
        SpecParseErrorCollector errors)
    {
        var refused = false;
        foreach (var property in mediaObject.EnumerateObject().Where(static property => property.Name is not "schema"))
        {
            errors.Add(location, $"unknown media-object key '{property.Name}'");
            refused = true;
        }

        return refused;
    }

    private static bool TryReadOptionalBoolean(JsonElement owner,
        string name,
        string location,
        SpecParseErrorCollector errors,
        out bool value)
    {
        value = false;
        if (!owner.TryGetProperty(name, out var property))
        {
            return true;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add(location, $"{name} must be a boolean");
            return false;
        }

        value = property.GetBoolean();
        return true;
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

    private static void ReadSchemas(JsonElement root,
        SchemaNodeParser parser,
        SortedDictionary<string, SchemaNode> graph,
        SpecParseErrorCollector errors)
    {
        if (!root.TryGetProperty("components", out var components))
        {
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
        IEnumerable<SpecOperation> operations,
        SpecParseErrorCollector errors)
    {
        foreach (var (root, node) in graph)
        {
            ValidateNodeDanglingRefs($"schema '{root}'", node, graph, errors);
        }

        foreach (var operation in operations)
        {
            ValidateOperationDanglingRefs(operation, graph, errors);
        }
    }

    private static void ValidateOperationDanglingRefs(SpecOperation operation,
        IReadOnlyDictionary<string, SchemaNode> graph,
        SpecParseErrorCollector errors)
    {
        var operationLocation = $"operation '{operation.OperationId}'";
        foreach (var parameter in operation.Parameters)
        {
            ValidateNodeDanglingRefs($"{operationLocation} parameter '{parameter.Name}'", parameter.Schema, graph, errors);
        }

        if (operation.RequestBody is { } requestBody)
        {
            ValidateNodeDanglingRefs($"{operationLocation} requestBody", requestBody.Schema, graph, errors);
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
