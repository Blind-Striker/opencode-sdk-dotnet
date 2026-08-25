using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>Binds the <see cref="OperationPlan.RequestBody"/> facet from the operation's declared body.</summary>
internal sealed class RequestBodyFacetBinder(OperationFacetContext context)
{
    private readonly OperationFacetContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public RequestBodyPlan? Bind()
    {
        var body = _context.Operation.RequestBody;
        if (body is null)
        {
            return null;
        }

        if (body.ContentType is not { IsJson: true })
        {
            _context.Refuse("the request body must carry a JSON schema");
            return null;
        }

        if (!body.IsRequired)
        {
            _context.Refuse("the request body must be declared required");
            return null;
        }

        if (body.Schema is not RefNode reference || _context.Resolve(body.Schema) is not ObjectNode target)
        {
            _context.Refuse("the request body must reference an object schema");
            return null;
        }

        if (!_context.TypeNames.TryGetValue(reference.Target, out var typeName))
        {
            _context.Errors.Add(BindingErrorCategory.Naming, reference.Target, "request body schema has no unique C# type name");
            return null;
        }

        // The name resolver claims every selected body root with the operation-derived
        // request name; a mismatch means the ownership map and this binding disagree.
        var expected = OperationNamePolicy.RequestTypeName(_context.Operation);
        if (!string.Equals(typeName, expected, StringComparison.Ordinal))
        {
            _context.Errors.Add(
                BindingErrorCategory.Naming,
                reference.Target,
                $"request body resolved to '{typeName}' instead of the operation-derived '{expected}'");
            return null;
        }

        return new RequestBodyPlan
        {
            TypeName = typeName,
            ParameterName = "request",
            IsOptional = target.Properties.All(static property => !property.IsRequired),
        };
    }
}
