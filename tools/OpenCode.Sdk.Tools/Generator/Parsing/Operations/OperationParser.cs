using System.Collections.ObjectModel;
using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

namespace OpenCode.Sdk.Tools.Generator.Parsing.Operations;

internal sealed class OperationParser
{
    private readonly SchemaNodeParser _schemaParser;
    private readonly SpecParseErrorCollector _errors;

    private IReadOnlyList<(string Location, SchemaNode Node)> _schemaRoots =
        [];

    public OperationParser(SchemaNodeParser schemaParser, SpecParseErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(schemaParser);
        ArgumentNullException.ThrowIfNull(errors);

        _schemaParser = schemaParser;
        _errors = errors;
    }

    public IReadOnlyList<(string Location, SchemaNode Node)> SchemaRoots => _schemaRoots;

    public IReadOnlyList<SpecOperation> Parse(JsonElement root)
    {
        List<SpecOperation> operations = [];
        HashSet<string> operationIds = new(StringComparer.Ordinal);
        if (root.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add("document", "root must be an object");
            return Complete(operations);
        }

        if (!root.TryGetProperty("paths", out var paths))
        {
            return Complete(operations);
        }

        if (paths.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add("document", "'paths' must be an object");
            return Complete(operations);
        }

        foreach (var pathItem in paths.EnumerateObject())
        {
            ReadPathItem(pathItem, operations, operationIds);
        }

        return Complete(operations);
    }

    private ReadOnlyCollection<SpecOperation> Complete(List<SpecOperation> operations)
    {
        var frozenOperations = operations.AsReadOnly();
        List<(string Location, SchemaNode Node)> roots = [];
        foreach (var operation in frozenOperations)
        {
            var operationLocation = $"operation '{operation.OperationId}'";
            roots.AddRange(operation.Parameters.Select(parameter =>
                ($"{operationLocation} parameter '{parameter.Name}'", parameter.Schema)));
            if (operation.RequestBody is { } requestBody)
            {
                roots.Add(($"{operationLocation} requestBody", requestBody.Schema));
            }
        }

        _schemaRoots = roots.AsReadOnly();
        return frozenOperations;
    }

    private void ReadPathItem(JsonProperty pathItem,
        List<SpecOperation> operations,
        HashSet<string> operationIds)
    {
        var pathLocation = $"path '{pathItem.Name}'";
        var hasWildcardPath = ReadWildcardPath(pathItem.Name, pathLocation);
        if (pathItem.Value.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(pathLocation, "path item must be an object");
            return;
        }

        foreach (var method in pathItem.Value.EnumerateObject())
        {
            if (!IsSupportedMethod(method.Name))
            {
                _errors.Add(pathLocation, $"unknown path-item key '{method.Name}'");
                continue;
            }

            var operation = ReadOperation(method.Value,
                pathItem.Name,
                method.Name,
                hasWildcardPath,
                operationIds);
            if (operation is not null)
            {
                operations.Add(operation);
            }
        }
    }

    private static bool IsSupportedMethod(string method) => method is "get" or "put" or "post" or "delete" or "patch";

    private bool ReadWildcardPath(string path, string location)
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

        _errors.Add(location, "wildcard is only valid as a terminal '/*' path segment");
        return false;
    }

    private SpecOperation? ReadOperation(JsonElement operation,
        string path,
        string method,
        bool hasWildcardPath,
        HashSet<string> operationIds)
    {
        var pathMethodLocation = $"path '{path}' method '{method}'";
        if (operation.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(pathMethodLocation, "operation must be an object");
            return null;
        }

        var operationId = ReadOperationId(operation, pathMethodLocation);
        var location = operationId is null ? pathMethodLocation : $"operation '{operationId}'";
        var hasKnownKeys = !RefuseUnsupportedOperationKeys(operation, location);
        if (operationId is null
            || !TryReadOperationIdentity(operationId, location, out var surface, out var segments))
        {
            return null;
        }

        if (!operationIds.Add(operationId))
        {
            _errors.Add(location, $"duplicate operationId '{operationId}'");
        }

        var hasValidSummary = TryReadOptionalString(operation, "summary", location, out var summary);
        var hasValidDescription = TryReadOptionalString(operation, "description", location, out var description);
        var hasValidDeprecated = TryReadDeprecated(operation, location, out var isDeprecated);
        var hasValidWebSocket = TryReadWebSocket(operation, location, out var isWebSocket);
        var hasValidParameters = TryReadParameters(operation,
            path,
            operationId,
            location,
            out var parameters);
        var hasValidRequestBody = TryReadRequestBody(operation,
            operationId,
            location,
            out var requestBody);
        if (!hasKnownKeys
            || !hasValidSummary
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

    private string? ReadOperationId(JsonElement operation, string location)
    {
        if (!operation.TryGetProperty("operationId", out var operationIdElement)
            || operationIdElement.ValueKind is not JsonValueKind.String)
        {
            _errors.Add(location, "operationId must be a non-empty string");
            return null;
        }

        var operationId = operationIdElement.GetString();
        if (string.IsNullOrWhiteSpace(operationId))
        {
            _errors.Add(location, "operationId must be a non-empty string");
            return null;
        }

        return operationId;
    }

    private bool RefuseUnsupportedOperationKeys(JsonElement operation, string location)
    {
        var refused = false;
        foreach (var propertyName in operation
                     .EnumerateObject()
                     .Select(static property => property.Name)
                     .Where(static propertyName => !IsSupportedOperationKey(propertyName)))
        {
            _errors.Add(location, $"unknown operation key '{propertyName}'");
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

    private bool TryReadParameters(JsonElement operation,
        string path,
        string operationId,
        string location,
        out IReadOnlyList<SpecParameter> parameters)
    {
        List<SpecParameter> parsedParameters = [];
        parameters = [];
        if (!operation.TryGetProperty("parameters", out var parameterElements))
        {
            var valid = ValidatePathParameters(path, parsedParameters, location);
            parameters = parsedParameters.AsReadOnly();
            return valid;
        }

        if (parameterElements.ValueKind is not JsonValueKind.Array)
        {
            _errors.Add(location, "parameters must be an array");
            return false;
        }

        HashSet<string> parameterKeys = new(StringComparer.Ordinal);
        var hasValidParameters = true;
        foreach (var parameterElement in parameterElements.EnumerateArray())
        {
            if (TryReadParameter(parameterElement,
                    operationId,
                    location,
                    parameterKeys,
                    out var parameter))
            {
                parsedParameters.Add(parameter!);
            }
            else
            {
                hasValidParameters = false;
            }
        }

        var hasValidPathParameters = ValidatePathParameters(path, parsedParameters, location);
        parameters = parsedParameters.AsReadOnly();
        return hasValidPathParameters && hasValidParameters;
    }

    private bool TryReadParameter(JsonElement element,
        string operationId,
        string operationLocation,
        HashSet<string> parameterKeys,
        out SpecParameter? parameter)
    {
        parameter = null;
        if (element.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(operationLocation, "parameter must be an object");
            return false;
        }

        var hasValidName = TryReadParameterName(element, operationLocation, out var name);
        var location = hasValidName ? $"{operationLocation} parameter '{name}'" : $"{operationLocation} parameter";
        var hasKnownKeys = !RefuseUnsupportedParameterKeys(element, location);
        var hasValidLocation = TryReadParameterLocation(element, location, out var parameterLocation);
        if (!hasValidName || !hasValidLocation || !TryAddParameterKey(name, parameterLocation, parameterKeys, location))
        {
            return false;
        }

        var hasValidRequired = TryReadOptionalBoolean(element, "required", location, out var isRequired);
        var hasValidStyle = TryReadDeepObject(element, location, out var isDeepObject);
        var hasValidSchema = TryReadParameterSchema(element,
            operationId,
            name,
            location,
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

    private bool TryReadParameterName(JsonElement parameter, string location, out string name)
    {
        name = string.Empty;
        if (!parameter.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind is not JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            _errors.Add(location, "parameter name must be a non-empty string");
            return false;
        }

        name = nameElement.GetString()!;
        return true;
    }

    private bool RefuseUnsupportedParameterKeys(JsonElement parameter, string location)
    {
        var refused = false;
        foreach (var propertyName in parameter
                     .EnumerateObject()
                     .Select(static property => property.Name)
                     .Where(static propertyName =>
                         propertyName is not ("name" or "in" or "schema" or "required" or "style" or "explode")))
        {
            _errors.Add(location, $"unknown parameter key '{propertyName}'");
            refused = true;
        }

        return refused;
    }

    private bool TryReadParameterLocation(JsonElement parameter,
        string location,
        out SpecParameterLocation parameterLocation)
    {
        parameterLocation = default;
        if (!parameter.TryGetProperty("in", out var locationElement)
            || locationElement.ValueKind is not JsonValueKind.String)
        {
            _errors.Add(location, "parameter location must be a string");
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
                _errors.Add(location, $"unknown parameter location '{value}'");
                return false;
        }
    }

    private bool TryAddParameterKey(string name,
        SpecParameterLocation parameterLocation,
        HashSet<string> parameterKeys,
        string location)
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

        _errors.Add(location, $"duplicate parameter '{name}' in '{locationName}'");
        return false;
    }

    private bool TryReadDeepObject(JsonElement parameter, string location, out bool isDeepObject)
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
            _errors.Add(location, "parameter style and explode must be specified together");
            return false;
        }

        if (style.ValueKind is not JsonValueKind.String
            || !string.Equals(style.GetString(), "deepObject", StringComparison.Ordinal))
        {
            _errors.Add(location, $"unknown parameter style '{ReadJsonText(style)}'");
            return false;
        }

        if (explode.ValueKind is not JsonValueKind.True)
        {
            _errors.Add(location, "deepObject explode must be literal true");
            return false;
        }

        isDeepObject = true;
        return true;
    }

    private static string ReadJsonText(JsonElement value) => value.ValueKind is JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : value.GetRawText();

    private bool TryReadParameterSchema(JsonElement parameter,
        string operationId,
        string name,
        string location,
        out SchemaNode? schema)
    {
        schema = null;
        if (!parameter.TryGetProperty("schema", out var schemaElement))
        {
            _errors.Add(location, "parameter schema is required");
            return false;
        }

        schema = _schemaParser.Parse(schemaElement, string.Concat("op:", operationId), $"/parameters/{name}");
        return schema is not null;
    }

    private bool ValidatePathParameters(string path,
        IReadOnlyList<SpecParameter> parameters,
        string location)
    {
        var hasValidTemplate = TryReadPathTokens(path, location, out var pathTokens);
        HashSet<string> declaredPathNames = new(
            parameters
                .Where(static parameter => parameter.Location is SpecParameterLocation.Path)
                .Select(static parameter => parameter.Name),
            StringComparer.Ordinal);
        HashSet<string> tokenNames = new(pathTokens, StringComparer.Ordinal);
        var valid = hasValidTemplate;
        foreach (var pathToken in pathTokens.Where(pathToken => !declaredPathNames.Contains(pathToken)))
        {
            _errors.Add(location, $"path template token '{{{pathToken}}}' has no declared path parameter");
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

            _errors.Add(location, $"declared path parameter '{parameterName}' is missing from the path template");
            valid = false;
        }

        return valid;
    }

    private bool TryReadPathTokens(string path, string location, out IReadOnlyList<string> tokens)
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
                _errors.Add(location, "path template contains an unclosed token");
                return false;
            }

            var closeIndex = openIndex + closeOffset + 1;
            var token = path[(openIndex + 1)..closeIndex];
            if (token.Length == 0)
            {
                _errors.Add(location, "path template token must not be empty");
                return false;
            }

            if (seenTokens.Add(token))
            {
                parsedTokens.Add(token);
            }

            searchIndex = closeIndex + 1;
        }
    }

    private bool TryReadRequestBody(JsonElement operation,
        string operationId,
        string operationLocation,
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
            _errors.Add(location, "requestBody must be an object");
            return false;
        }

        var hasKnownKeys = !RefuseUnsupportedRequestBodyKeys(requestBodyElement, location);
        var hasValidRequired = TryReadOptionalBoolean(requestBodyElement, "required", location, out var isRequired);
        var hasValidContent = TryReadRequestContent(requestBodyElement,
            operationId,
            location,
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

    private bool RefuseUnsupportedRequestBodyKeys(JsonElement requestBody, string location)
    {
        var refused = false;
        foreach (var propertyName in requestBody
                     .EnumerateObject()
                     .Select(static property => property.Name)
                     .Where(static propertyName => propertyName is not ("content" or "required")))
        {
            _errors.Add(location, $"unknown requestBody key '{propertyName}'");
            refused = true;
        }

        return refused;
    }

    private bool TryReadRequestContent(JsonElement requestBody,
        string operationId,
        string location,
        out SpecMediaType? contentType,
        out SchemaNode? schema)
    {
        contentType = null;
        schema = null;
        if (!requestBody.TryGetProperty("content", out var content))
        {
            _errors.Add(location, "requestBody content must contain exactly one media entry");
            return false;
        }

        if (content.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(location, "requestBody content must be an object");
            return false;
        }

        var mediaEntries = content.EnumerateObject();
        if (!mediaEntries.MoveNext())
        {
            _errors.Add(location, "requestBody content must contain exactly one media entry");
            return false;
        }

        var mediaEntry = mediaEntries.Current;
        if (mediaEntries.MoveNext())
        {
            _errors.Add(location, "requestBody content must contain exactly one media entry");
            return false;
        }

        var mediaLocation = $"{location} content '{mediaEntry.Name}'";
        var hasValidContentType = TryCreateMediaType(mediaEntry.Name, mediaLocation, out contentType);
        var hasValidMediaObject = TryReadRequestMediaObject(mediaEntry.Value,
            operationId,
            mediaLocation,
            out schema);
        return hasValidContentType && hasValidMediaObject;
    }

    private bool TryCreateMediaType(string raw, string location, out SpecMediaType? mediaType)
    {
        try
        {
            mediaType = SpecMediaType.Create(raw);
            return true;
        }
        catch (ArgumentException exception)
        {
            _errors.Add(location, exception.Message);
            mediaType = null;
            return false;
        }
    }

    private bool TryReadRequestMediaObject(JsonElement mediaObject,
        string operationId,
        string location,
        out SchemaNode? schema)
    {
        schema = null;
        if (mediaObject.ValueKind is not JsonValueKind.Object)
        {
            _errors.Add(location, "media entry must be an object");
            return false;
        }

        var hasKnownKeys = !RefuseUnsupportedRequestMediaKeys(mediaObject, location);
        if (!mediaObject.TryGetProperty("schema", out var schemaElement))
        {
            _errors.Add(location, "media entry schema is required");
            return false;
        }

        schema = _schemaParser.Parse(schemaElement, string.Concat("op:", operationId), "/requestBody");
        return hasKnownKeys && schema is not null;
    }

    private bool RefuseUnsupportedRequestMediaKeys(JsonElement mediaObject, string location)
    {
        var refused = false;
        foreach (var property in mediaObject.EnumerateObject().Where(static property => property.Name is not "schema"))
        {
            _errors.Add(location, $"unknown media-object key '{property.Name}'");
            refused = true;
        }

        return refused;
    }

    private bool TryReadOptionalBoolean(JsonElement owner, string name, string location, out bool value)
    {
        value = false;
        if (!owner.TryGetProperty(name, out var property))
        {
            return true;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            _errors.Add(location, $"{name} must be a boolean");
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private bool TryReadOperationIdentity(string operationId,
        string location,
        out SpecSurface surface,
        out IReadOnlyList<string> segments)
    {
        var parsedSegments = operationId.Split('.');
        if (string.Equals(parsedSegments[0], "v2", StringComparison.Ordinal))
        {
            surface = SpecSurface.Modern;
            segments = Array.AsReadOnly(parsedSegments[1..]);
        }
        else
        {
            surface = SpecSurface.Legacy;
            segments = Array.AsReadOnly(parsedSegments);
        }

        if (segments.Count > 0)
        {
            return true;
        }

        _errors.Add(location, "operationId must have at least one segment after 'v2'");
        return false;
    }

    private bool TryReadOptionalString(JsonElement operation,
        string name,
        string location,
        out string? value)
    {
        value = null;
        if (!operation.TryGetProperty(name, out var property))
        {
            return true;
        }

        if (property.ValueKind is not JsonValueKind.String)
        {
            _errors.Add(location, $"{name} must be a string");
            return false;
        }

        value = property.GetString();
        return true;
    }

    private bool TryReadDeprecated(JsonElement operation, string location, out bool isDeprecated)
    {
        isDeprecated = false;
        if (!operation.TryGetProperty("deprecated", out var deprecated))
        {
            return true;
        }

        if (deprecated.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            _errors.Add(location, "deprecated must be a boolean");
            return false;
        }

        isDeprecated = deprecated.GetBoolean();
        return true;
    }

    private bool TryReadWebSocket(JsonElement operation, string location, out bool isWebSocket)
    {
        isWebSocket = false;
        if (!operation.TryGetProperty("x-websocket", out var webSocket))
        {
            return true;
        }

        if (webSocket.ValueKind is not JsonValueKind.True)
        {
            _errors.Add(location, "x-websocket must be literal true");
            return false;
        }

        isWebSocket = true;
        return true;
    }
}
