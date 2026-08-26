using System.Globalization;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// The fail-closed wire-shape wall: HTTP method, path, body presence, path-parameter and
/// status shapes are refused up front so every facet binder reads a known-good operation.
/// </summary>
internal sealed class OperationWireShapeWall(OperationFacetContext context)
{
    private readonly OperationFacetContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public bool Check()
    {
        var before = _context.Errors.Count;
        var operation = _context.Operation;
        var isGet = string.Equals(operation.Method, "get", StringComparison.Ordinal);
        var isPost = string.Equals(operation.Method, "post", StringComparison.Ordinal);
        var isDelete = string.Equals(operation.Method, "delete", StringComparison.Ordinal);
        var isPatch = string.Equals(operation.Method, "patch", StringComparison.Ordinal);
        var isPut = string.Equals(operation.Method, "put", StringComparison.Ordinal);
        if (!isGet && !isPost && !isDelete && !isPatch && !isPut)
        {
            _context.Refuse($"HTTP method '{operation.Method}' is not supported");
        }

        if (operation.HasWildcardPath)
        {
            _context.Refuse("wildcard paths are not supported in M1");
        }

        if (operation.IsWebSocket)
        {
            _context.Refuse("WebSocket operations are not supported in M1");
        }

        if ((isGet || isDelete) && operation.RequestBody is not null)
        {
            _context.Refuse($"{operation.Method.ToUpperInvariant()} operations must not carry a request body");
        }

        // The pin demonstrates bodyless POST across twelve operations, so POST admits both
        // shapes; PATCH and PUT keep the body requirement until the pin shows otherwise.
        if ((isPatch || isPut) && operation.RequestBody is null)
        {
            _context.Refuse($"{operation.Method.ToUpperInvariant()} operations must carry a request body");
        }

        CheckParameterShapes();
        CheckStatusShape();
        return _context.Errors.Count == before;
    }

    private void CheckParameterShapes()
    {
        // Header parameters ingest faithfully but no runtime channel carries them yet; a
        // selected operation refuses here until the location/PTY arc gives headers an owner.
        foreach (var parameter in _context.Operation.Parameters.Where(static parameter => parameter.Location is SpecParameterLocation.Header))
        {
            _context.Refuse($"header parameter '{parameter.Name}' has no runtime channel");
        }

        foreach (var parameter in _context.Operation.Parameters.Where(static parameter => parameter.Location is SpecParameterLocation.Path))
        {
            if (parameter is not { IsRequired: true, IsDeepObject: false })
            {
                _context.Refuse($"path parameter '{parameter.Name}' must be required and plainly encoded");
                continue;
            }

            if (_context.Resolve(parameter.Schema) is not PrimitiveNode { Kind: PrimitiveKind.String })
            {
                _context.Refuse($"path parameter '{parameter.Name}' must be a plain string");
            }
        }
    }

    private void CheckStatusShape()
    {
        var successes = _context.Operation.Responses.Where(static response => response.StatusCode is >= 200 and < 300).ToArray();
        if (successes.Length is not 1)
        {
            _context.Refuse("operation must declare exactly one success response");
        }
        else if (successes[0].StatusCode is not (200 or 204))
        {
            _context.Refuse("the success response must use status 200 or a content-free 204");
        }

        foreach (var response in _context.Operation.Responses
                     .Where(static response => response.StatusCode is < 200 or (>= 300 and < 400)))
        {
            _context.Refuse($"status '{response.StatusCode.ToString(CultureInfo.InvariantCulture)}' must be an error status");
        }
    }
}
