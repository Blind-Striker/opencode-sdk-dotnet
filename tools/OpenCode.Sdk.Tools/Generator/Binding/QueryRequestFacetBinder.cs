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
            var property = parameter.IsDeepObject ? BindLocationSelector(parameter) : BindValue(parameter);
            if (property is not null)
            {
                properties.Add(property);
            }
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

    /// <summary>
    /// Binds one ordinary query value. Requiredness rides the property rather than a wall:
    /// the emitted member is C# <c>required</c> and non-nullable, and an optional parameter
    /// binds identically whether or not its schema admits JSON null, because an unset member
    /// is omitted from the wire either way.
    /// </summary>
    private QueryPropertyPlan? BindValue(SpecParameter parameter)
    {
        var declared = _context.Resolve(parameter.Schema);
        if (declared is NullableNode { Format: not null })
        {
            return _context.RefuseNull<QueryPropertyPlan>($"query parameter '{parameter.Name}' has an unsupported schema shape");
        }

        var value = declared is NullableNode nullable ? nullable.Inner : parameter.Schema;
        var enumTypeName = ResolveEnumTypeName(parameter, value, out var isEnum);
        if (isEnum && enumTypeName is null)
        {
            return null;
        }

        var kind = isEnum ? QueryValueKind.Enum : ResolveQueryValueKind(value);
        if (kind is null)
        {
            return _context.RefuseNull<QueryPropertyPlan>($"query parameter '{parameter.Name}' has an unsupported schema shape");
        }

        return new QueryPropertyPlan
        {
            WireName = parameter.Name,
            PropertyName = CSharpNamePolicy.ToPascalCase(parameter.Name),
            Kind = kind.Value,
            EnumTypeName = enumTypeName,
            Description = declared.Description ?? _context.Resolve(value).Description,
            IsRequired = parameter.IsRequired,
            IsInherited = false,
        };
    }

    /// <summary>
    /// Names the generated enum a parameter binds to. The type-name map is the same one the
    /// model closure emits from, so a key the map does not carry means the enum has no model
    /// and the operation refuses rather than typing a property against a missing type.
    /// </summary>
    private string? ResolveEnumTypeName(SpecParameter parameter, SchemaNode value, out bool isEnum)
    {
        var key = QueryEnumShapePolicy.ResolveModelKey(value, _context.Document.Schemas);
        isEnum = key is not null;
        if (key is null)
        {
            return null;
        }

        return _context.TypeNames.TryGetValue(key, out var typeName)
            ? typeName
            : _context.RefuseNull($"query parameter '{parameter.Name}' binds an enum that has no generated model");
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
            IsRequired = false,
            IsInherited = false,
        };
    }

    /// <summary>
    /// The fail-closed profile wall: an operation derives from the <c>ListRequest</c> base
    /// only when its wire query parameters are exactly the optional cursor-pagination trio.
    /// </summary>
    private static bool MatchesListRequestProfile(List<QueryPropertyPlan> properties) =>
        properties.Count is 3
        && properties.Any(static property => property is { WireName: "limit", Kind: QueryValueKind.Text, IsRequired: false })
        && properties.Any(static property => property is { WireName: "order", Kind: QueryValueKind.ListOrder, IsRequired: false })
        && properties.Any(static property => property is { WireName: "cursor", Kind: QueryValueKind.Text, IsRequired: false });

    private QueryValueKind? ResolveQueryValueKind(SchemaNode value)
    {
        return _context.Resolve(value) switch
        {
            PrimitiveNode { Kind: PrimitiveKind.String, Format: null } => QueryValueKind.Text,
            EnumNode node => QueryEnumShapePolicy.ResolveSpineKind(node),
            UnionNode { Classification: UnionClassification.Structural, Format: null, Branches: [var first, var second] }
                when SpineShapePolicy.IsParentFilterShape(_context, first, second) => QueryValueKind.SessionParentFilter,
            _ => null,
        };
    }
}
