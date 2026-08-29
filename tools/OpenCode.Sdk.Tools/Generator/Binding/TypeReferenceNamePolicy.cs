using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal static class TypeReferenceNamePolicy
{
    public static string Format(TypeReferencePlan type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var name = type switch
        {
            NamedTypeReferencePlan named => named.Name,
            SpecialNumberTypeReferencePlan => "double",
            BinaryTypeReferencePlan => "ReadOnlyMemory<byte>",
            ListTypeReferencePlan list => $"IReadOnlyList<{Format(list.ElementType)}>",
            DictionaryTypeReferencePlan dictionary => $"IReadOnlyDictionary<string, {Format(dictionary.ValueType)}>",
            _ => throw new InvalidOperationException($"Unknown type-reference plan '{type.GetType().Name}'."),
        };
        return type.IsNullable ? $"{name}?" : name;
    }
}
