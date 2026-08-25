using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Recognizes the wire shapes the hand-written runtime spine fixes — always structurally,
/// never by parameter or property name — so a spec drift lands as a bind-time refusal
/// instead of a silently misserialized member.
/// </summary>
internal static class SpineShapePolicy
{
    /// <summary>
    /// Recognizes the dual-channel location selector structurally — exactly the
    /// optional-nullable string <c>directory</c> and <c>workspace</c> members — so the
    /// route serializer's fixed member set stays safe.
    /// </summary>
    public static bool IsLocationSelectorShape(OperationFacetContext context, SchemaNode schema)
    {
        if (context.Resolve(schema) is not ObjectNode selector
            || selector.Format is not null
            || selector.Properties.Count is not 2
            || selector.Properties.Select(static property => property.Name).Distinct(StringComparer.Ordinal).Count() is not 2)
        {
            return false;
        }

        return selector.Properties.All(property => property is { IsRequired: false, Name: "directory" or "workspace" }
                                                   && IsNullableUnformattedString(context, property.Schema));
    }

    /// <summary>Recognizes the wire cursor contract: exactly optional-nullable string <c>previous</c> and <c>next</c>.</summary>
    public static bool IsListCursorShape(OperationFacetContext context, SchemaNode schema)
    {
        if (context.Resolve(schema) is not ObjectNode cursor
            || cursor.Format is not null
            || cursor.Properties.Count is not 2
            || cursor.Properties.Select(static property => property.Name).Distinct(StringComparer.Ordinal).Count() is not 2)
        {
            return false;
        }

        return cursor.Properties.All(property => property is { IsRequired: false, Name: "previous" or "next" }
                                                 && IsNullableUnformattedString(context, property.Schema));
    }

    public static bool IsNullableUnformattedString(OperationFacetContext context, SchemaNode schema) =>
        context.Resolve(schema) is NullableNode { Format: null, Inner: var inner }
        && context.Resolve(inner) is PrimitiveNode { Kind: PrimitiveKind.String, Format: null };

    /// <summary>
    /// Recognizes the parent-filter wire shape — a patterned identifier string beside the
    /// literal <c>"null"</c> — structurally, never by parameter name.
    /// </summary>
    public static bool IsParentFilterShape(OperationFacetContext context, SchemaNode first, SchemaNode second)
    {
        var left = context.Resolve(first);
        var right = context.Resolve(second);
        return (IsIdentifierString(left) && IsNullLiteral(right))
               || (IsIdentifierString(right) && IsNullLiteral(left));
    }

    private static bool IsIdentifierString(SchemaNode schema) => schema is PrimitiveNode { Kind: PrimitiveKind.String, Format: null };

    private static bool IsNullLiteral(SchemaNode schema) => schema is LiteralNode { Kind: LiteralKind.String, Value: "null", Format: null };
}
