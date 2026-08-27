using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Binds the <see cref="OperationPlan.Pagination"/> facet: a cursor-list operation whose
/// request derives from the <c>ListRequest</c> base additionally gains an enumeration method.
/// </summary>
internal sealed class PaginationFacetBinder(OperationFacetContext context)
{
    private readonly OperationFacetContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public PaginationPlan? Bind(string methodName, IReadOnlyList<OperationParameterPlan>? parameters,
        IReadOnlyList<DeclaredHeaderPlan> declaredHeaders, QueryRequestPlan? queryRequest, RequestBodyPlan? requestBody,
        EnvelopePlan? envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(declaredHeaders);

        if (queryRequest is not { DerivesFromListRequest: true, RidesRequestBody: false }
            || envelope is not
            {
                Kind: EnvelopeKind.CursorList,
                PayloadName: { } payloadName,
                PayloadTypeName: { } itemTypeName,
            })
        {
            return null;
        }

        if (!string.Equals(_context.Operation.Method, "get", StringComparison.Ordinal)
            || requestBody is not null
            || parameters is null
            || parameters.Any(static parameter => !parameter.IsHandleParameter))
        {
            return _context.RefuseNull<PaginationPlan>(
                "cursor pagination currently requires a GET operation with no body or unbound route parameters");
        }

        // The traversal core takes the one-page method as a fixed three-argument delegate,
        // so an extra emitted parameter would break the method-group conversion.
        if (declaredHeaders.Count > 0)
        {
            return _context.RefuseNull<PaginationPlan>("cursor pagination cannot carry a declared header parameter");
        }

        var enumerationMethodName = OperationNamePolicy.EnumerationMethodName(methodName);
        if (enumerationMethodName is null)
        {
            _context.Errors.Add(
                BindingErrorCategory.Naming,
                _context.Operation.OperationId,
                $"cursor-list method '{methodName}' must be an asynchronous List method before its enumeration name can be derived");
            return null;
        }

        return new PaginationPlan
        {
            MethodName = enumerationMethodName,
            RequestTypeName = queryRequest.TypeName,
            ItemTypeName = itemTypeName,
            PayloadName = payloadName,
        };
    }
}
