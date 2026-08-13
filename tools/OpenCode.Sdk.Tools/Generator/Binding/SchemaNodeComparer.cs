using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Structural equality over projected schema trees; node kinds without an explicit rule
/// compare unequal, so aliasing them refuses instead of guessing.
/// </summary>
internal static class SchemaNodeComparer
{
    public static bool DeepEquals(SchemaNode left, SchemaNode right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return (left, right) switch
        {
            (PrimitiveNode a, PrimitiveNode b) => a.Kind == b.Kind,
            (LiteralNode a, LiteralNode b) => a.Kind == b.Kind && a.Dialect == b.Dialect
                && string.Equals(a.Value, b.Value, StringComparison.Ordinal),
            (EnumNode a, EnumNode b) => a.Values.SequenceEqual(b.Values, StringComparer.Ordinal),
            (RefNode a, RefNode b) => string.Equals(a.Target, b.Target, StringComparison.Ordinal),
            (NullableNode a, NullableNode b) => DeepEquals(a.Inner, b.Inner),
            (ArrayNode a, ArrayNode b) => DeepEquals(a.Item, b.Item),
            (TupleNode a, TupleNode b) => SequenceEquals(a.Items, b.Items),
            (DictionaryNode a, DictionaryNode b) => DeepEquals(a.Value, b.Value),
            (UnionNode a, UnionNode b) => a.Keyword == b.Keyword && a.Classification == b.Classification
                && SequenceEquals(a.Branches, b.Branches),
            (ObjectNode a, ObjectNode b) => ObjectsEqual(a, b),
            (FreeFormObjectNode, FreeFormObjectNode) => true,
            (UnrestrictedNode, UnrestrictedNode) => true,
            _ => false,
        };
    }

    private static bool ObjectsEqual(ObjectNode left, ObjectNode right) =>
        left.AdditionalProperties == right.AdditionalProperties
        && left.ErrorStyle == right.ErrorStyle
        && AdditionalSchemasEqual(left, right)
        && left.Properties.Count == right.Properties.Count
        && left.Properties.Zip(right.Properties).All(static pair => PropertiesEqual(pair.First, pair.Second))
        && left.LiteralMarkers.Count == right.LiteralMarkers.Count
        && left.LiteralMarkers.Zip(right.LiteralMarkers).All(static pair => MarkersEqual(pair.First, pair.Second));

    private static bool AdditionalSchemasEqual(ObjectNode left, ObjectNode right) =>
        (left.AdditionalPropertiesSchema, right.AdditionalPropertiesSchema) switch
        {
            (null, null) => true,
            (not null, not null) => DeepEquals(left.AdditionalPropertiesSchema, right.AdditionalPropertiesSchema),
            _ => false,
        };

    private static bool PropertiesEqual(SpecProperty left, SpecProperty right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal)
        && left.IsRequired == right.IsRequired
        && DeepEquals(left.Schema, right.Schema);

    private static bool MarkersEqual(LiteralMarker left, LiteralMarker right) =>
        string.Equals(left.PropertyName, right.PropertyName, StringComparison.Ordinal)
        && left.Kind == right.Kind
        && string.Equals(left.Value, right.Value, StringComparison.Ordinal);

    private static bool SequenceEquals(IReadOnlyList<SchemaNode> left, IReadOnlyList<SchemaNode> right) =>
        left.Count == right.Count && left.Zip(right).All(static pair => DeepEquals(pair.First, pair.Second));
}
