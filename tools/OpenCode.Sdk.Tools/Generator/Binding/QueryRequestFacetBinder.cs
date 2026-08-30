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
        var binding = ResolveEnumBinding(parameter, value);
        if (binding is { IsEnum: true, TypeName: null })
        {
            return null;
        }

        var kind = binding.IsEnum ? QueryValueKind.Enum : ResolveQueryValueKind(value);
        if (kind is null)
        {
            return _context.RefuseNull<QueryPropertyPlan>($"query parameter '{parameter.Name}' has an unsupported schema shape");
        }

        return new QueryPropertyPlan
        {
            WireName = parameter.Name,
            PropertyName = CSharpNamePolicy.ToPascalCase(parameter.Name),
            Kind = kind.Value,
            EnumTypeName = binding.TypeName,
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
    private QueryEnumBinding ResolveEnumBinding(SpecParameter parameter, SchemaNode value)
    {
        var key = QueryEnumShapePolicy.ResolveModelKey(value, _context.Document.Schemas);
        if (key is null)
        {
            return QueryEnumBinding.NotAnEnum;
        }

        if (_context.TypeNames.TryGetValue(key, out var typeName))
        {
            return QueryEnumBinding.Named(typeName);
        }

        _ = _context.RefuseNull($"query parameter '{parameter.Name}' binds an enum that has no generated model");
        return QueryEnumBinding.Refused;
    }

    /// <summary>
    /// What the enum lookup found for one query parameter. Three outcomes ride one value - the
    /// parameter is not enum-valued, it binds a named generated enum, or it is an enum whose
    /// model is missing and has already been refused by name - so a caller cannot read the
    /// "is it an enum" answer apart from the name that belongs with it.
    /// </summary>
    private readonly record struct QueryEnumBinding(bool IsEnum, string? TypeName)
    {
        /// <summary>The parameter is not enum-valued; the ordinary value path owns it.</summary>
        public static QueryEnumBinding NotAnEnum => new(IsEnum: false, TypeName: null);

        /// <summary>The parameter is an enum with no generated model; the refusal is recorded.</summary>
        public static QueryEnumBinding Refused => new(IsEnum: true, TypeName: null);

        /// <summary>The parameter binds the named generated enum.</summary>
        public static QueryEnumBinding Named(string typeName) => new(IsEnum: true, typeName);
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
