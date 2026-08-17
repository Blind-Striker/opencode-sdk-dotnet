using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Reads the two union shapes that offer a consumer no choice to make. Branches that are
/// refinements of one primitive differ only in constraints this SDK does not enforce, so the
/// union is that primitive — an open enum is the common case. An object-or-array pair is how a
/// struct with no declared fields renders, and neither branch says what the content is, so it
/// is an object of unspecified content. Anything else stays a structural union and is refused.
/// </summary>
internal static class UnstructuredUnionPolicy
{
    public static SchemaNode? Collapse(UnionNode union)
    {
        ArgumentNullException.ThrowIfNull(union);

        var kinds = union.Branches.Select(PrimitiveKindOf).ToArray();
        if (kinds.Length > 1 && kinds[0] is { } first && Array.TrueForAll(kinds, kind => kind == first))
        {
            return new PrimitiveNode { Kind = first };
        }

        return union.Branches.Count is 2
               && union.Branches.Count(static branch => branch is FreeFormObjectNode) is 1
               && union.Branches.Count(static branch => branch is ArrayNode { Item: UnrestrictedNode }) is 1
            ? union.Branches.OfType<FreeFormObjectNode>().Single()
            : null;
    }

    private static PrimitiveKind? PrimitiveKindOf(SchemaNode branch) => branch switch
    {
        PrimitiveNode primitive => primitive.Kind,
        EnumNode => PrimitiveKind.String,
        LiteralNode { Kind: LiteralKind.String } => PrimitiveKind.String,
        LiteralNode { Kind: LiteralKind.Number } => PrimitiveKind.Number,
        LiteralNode { Kind: LiteralKind.Boolean } => PrimitiveKind.Boolean,
        _ => null,
    };
}
