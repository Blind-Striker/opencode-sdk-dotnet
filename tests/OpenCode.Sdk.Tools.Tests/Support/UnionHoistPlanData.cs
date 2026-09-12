using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Tests.Support;

/// <summary>
/// Bound-plan vocabulary for member-hoisting scenarios: a marked union, the records its arms
/// bind to, and the promoted envelope records an arm points at. The builders speak the plan the
/// hoist binder reads, so a test states the shape difference it is about and nothing else.
/// </summary>
internal static class UnionHoistPlanData
{
    public static UnionPlan Union(string name, params UnionVariantPlan[] variants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(variants);
        return new UnionPlan
        {
            Name = name,
            ConceptName = name[1..],
            Namespace = "OpenCode.Sdk.Models",
            UnknownTypeName = $"Unknown{name[1..]}",
            MarkerWireName = "type",
            MarkerName = "Type",
            MarkerKind = LiteralKind.String,
            Variants = variants,
        };
    }

    public static UnionVariantPlan Arm(string typeName) =>
        new()
        {
            TypeName = typeName,
            Tag = typeName,
            MarkerWireName = "type",
        };

    public static UnionVariantPlan NestedArm(string unionName) =>
        new()
        {
            TypeName = unionName,
            Tag = unionName,
            MarkerWireName = "type",
            IsNestedUnion = true,
        };

    /// <summary>A union arm's record: its literal tag plus the properties the scenario varies.</summary>
    public static ObjectModelPlan Variant(string typeName, string tag, params ModelPropertyPlan[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return Record(typeName, [StringLiteral("type", tag), .. properties]);
    }

    public static ObjectModelPlan Record(string typeName, params ModelPropertyPlan[] properties) =>
        new()
        {
            Name = typeName,
            Namespace = "OpenCode.Sdk.Models",
            Properties = properties,
        };

    /// <summary>A promoted durable envelope: one plain member plus the per-arm schema-version literal.</summary>
    public static ObjectModelPlan Envelope(string typeName, string version) =>
        Record(typeName, Property("seq", Named("long"), isRequired: true), NumberLiteral("version", version));

    public static ModelPropertyPlan Property(string wireName, TypeReferencePlan type, bool isRequired) =>
        new()
        {
            WireName = wireName,
            Name = PascalCase(wireName),
            Type = type,
            IsRequired = isRequired,
            IsLiteral = false,
        };

    public static ModelPropertyPlan StringLiteral(string wireName, string value) =>
        new()
        {
            WireName = wireName,
            Name = PascalCase(wireName),
            Type = Named("string"),
            IsRequired = true,
            IsLiteral = true,
            LiteralKind = LiteralKind.String,
            LiteralValue = value,
        };

    public static ModelPropertyPlan NumberLiteral(string wireName, string value) =>
        new()
        {
            WireName = wireName,
            Name = PascalCase(wireName),
            Type = Named("double"),
            IsRequired = true,
            IsLiteral = true,
            LiteralKind = LiteralKind.Number,
            LiteralValue = value,
        };

    public static NamedTypeReferencePlan Named(string name, bool isNullable = false) =>
        new()
        {
            Name = name,
            IsNullable = isNullable,
            JsonNullRepresentation = JsonNullRepresentation.ClrNull,
        };

    private static string PascalCase(string wireName) =>
        string.Concat(wireName[..1].ToUpperInvariant(), wireName[1..]);
}
