using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>Binds the <see cref="OperationPlan.QueryRequest"/> facet from the operation's query parameters.</summary>
internal sealed class QueryRequestFacetBinder(OperationFacetContext context)
{
    private readonly OperationFacetContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public QueryRequestPlan? Bind()
    {
        var query = _context.Operation.Parameters.Where(static parameter => parameter.Location is SpecParameterLocation.Query).ToArray();
        if (query.Length is 0)
        {
            return null;
        }

        var properties = new List<QueryPropertyPlan>(query.Length);
        foreach (var parameter in query)
        {
            if (parameter.IsDeepObject)
            {
                var location = BindLocationSelector(parameter);
                if (location is not null)
                {
                    properties.Add(location);
                }

                continue;
            }

            if (parameter.IsRequired)
            {
                _context.Refuse($"query parameter '{parameter.Name}' must be optional");
                continue;
            }

            if (_context.Resolve(parameter.Schema) is not NullableNode nullable)
            {
                _context.Refuse($"query parameter '{parameter.Name}' must admit null");
                continue;
            }

            var kind = nullable.Format is null ? ResolveQueryValueKind(nullable.Inner) : null;
            if (kind is null)
            {
                _context.Refuse($"query parameter '{parameter.Name}' has an unsupported schema shape");
                continue;
            }

            properties.Add(new QueryPropertyPlan
            {
                WireName = parameter.Name,
                PropertyName = CSharpNamePolicy.ToPascalCase(parameter.Name),
                Kind = kind.Value,
                Description = nullable.Description ?? _context.Resolve(nullable.Inner).Description,
                IsInherited = false,
            });
        }

        var duplicate = properties
            .GroupBy(static property => property.PropertyName, StringComparer.Ordinal)
            .FirstOrDefault(static property => property.Skip(1).Any());
        if (duplicate is not null)
        {
            _context.Errors.Add(
                BindingErrorCategory.Naming,
                _context.Operation.OperationId,
                $"multiple query parameters map to C# name '{duplicate.Key}'");
            return null;
        }

        if (MatchesListRequestProfile(properties))
        {
            properties =
            [
                .. properties.Select(static property => property with
                {
                    IsInherited = true
                })
            ];
        }

        return new QueryRequestPlan
        {
            TypeName = OperationNamePolicy.RequestTypeName(_context.Operation),
            DerivesFromListRequest = properties.Count > 0 && properties[0].IsInherited,
            Properties = properties,
        };
    }

    /// <summary>The one admitted deep-object encoding is the optional nullable location selector.</summary>
    private QueryPropertyPlan? BindLocationSelector(SpecParameter parameter)
    {
        if (parameter.IsRequired
            || _context.Resolve(parameter.Schema) is not NullableNode nullable
            || nullable.Format is not null
            || !SpineShapePolicy.IsLocationSelectorShape(_context, nullable.Inner))
        {
            return _context.RefuseNull<QueryPropertyPlan>(
                $"query parameter '{parameter.Name}' uses deep-object encoding outside the optional location selector shape");
        }

        return new QueryPropertyPlan
        {
            WireName = parameter.Name,
            PropertyName = CSharpNamePolicy.ToPascalCase(parameter.Name),
            Kind = QueryValueKind.Location,
            Description = parameter.Schema.Description,
            IsInherited = false,
        };
    }

    /// <summary>
    /// The fail-closed profile wall: an operation derives from the <c>ListRequest</c> base
    /// only when its wire query parameters are exactly the cursor-pagination trio.
    /// </summary>
    private static bool MatchesListRequestProfile(List<QueryPropertyPlan> properties) =>
        properties.Count is 3
        && properties.Any(static property => property is { WireName: "limit", Kind: QueryValueKind.Text })
        && properties.Any(static property => property is { WireName: "order", Kind: QueryValueKind.ListOrder })
        && properties.Any(static property => property is { WireName: "cursor", Kind: QueryValueKind.Text });

    private QueryValueKind? ResolveQueryValueKind(SchemaNode inner)
    {
        return _context.Resolve(inner) switch
        {
            PrimitiveNode { Kind: PrimitiveKind.String, Format: null } => QueryValueKind.Text,
            EnumNode { Values: ["asc", "desc"], Format: null } => QueryValueKind.ListOrder,
            EnumNode { Values: ["true", "false"] or ["false", "true"], Format: null } => QueryValueKind.BooleanText,
            UnionNode { Classification: UnionClassification.Structural, Format: null, Branches: [var first, var second] }
                when SpineShapePolicy.IsParentFilterShape(_context, first, second) => QueryValueKind.SessionParentFilter,
            _ => null,
        };
    }
}
