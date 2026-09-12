using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Decides whether a record's own property already satisfies the interface member hoisted from
/// it. A reference type answers a nullable annotation of itself, so the existing property is the
/// implementation; a value type against <c>T?</c>, and a promoted record against its hoisted
/// carrier, are different CLR types and need an explicit implementation instead.
/// </summary>
internal sealed class HoistedMemberSatisfactionPolicy(IReadOnlySet<string> valueTypeNames)
{
    /// <summary>The named types the emitters render as CLR value types beside the enums the pin declares.</summary>
    private static readonly string[] PrimitiveValueTypeNames = ["bool", "double", "long", "int", "JsonElement"];

    private readonly IReadOnlySet<string> _valueTypeNames = valueTypeNames ?? throw new ArgumentNullException(nameof(valueTypeNames));

    public bool RequiresExplicitImplementation(TypeReferencePlan own, TypeReferencePlan declared)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(declared);

        return own != declared && (own with { IsNullable = true } != declared || !IsReferenceType(own));
    }

    private bool IsReferenceType(TypeReferencePlan type) => type switch
    {
        ListTypeReferencePlan or DictionaryTypeReferencePlan => true,
        SpecialNumberTypeReferencePlan or BinaryTypeReferencePlan => false,
        NamedTypeReferencePlan named => !PrimitiveValueTypeNames.Contains(named.Name, StringComparer.Ordinal)
                                        && !_valueTypeNames.Contains(named.Name),
        _ => false,
    };
}
