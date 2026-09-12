using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Decides whether a record's own property already satisfies the interface member hoisted from
/// it. A reference type answers a nullable annotation of itself, so the existing property is the
/// implementation; a value type against <c>T?</c>, a promoted record against its hoisted carrier,
/// and a property carrying the tri-state <c>Optional&lt;T&gt;</c> wrapper are different CLR types
/// and need an explicit implementation instead.
/// </summary>
internal sealed class HoistedMemberSatisfactionPolicy(IReadOnlySet<string> valueTypeNames)
{
    /// <summary>The named types the emitters render as CLR value types beside the enums the pin declares.</summary>
    private static readonly string[] PrimitiveValueTypeNames = ["bool", "double", "long", "int", "JsonElement"];

    private readonly IReadOnlySet<string> _valueTypeNames = valueTypeNames ?? throw new ArgumentNullException(nameof(valueTypeNames));

    public bool RequiresExplicitImplementation(ModelPropertyPlan own, TypeReferencePlan declared)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(declared);

        // Optional<T?> is its own CLR type; the interface declares the unwrapped T?, so the
        // record answers it explicitly through the wrapper's Value.
        return own.EmitsOptionalWrapper
               || (own.Type != declared && (own.Type with { IsNullable = true } != declared || !IsReferenceType(own.Type)));
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
