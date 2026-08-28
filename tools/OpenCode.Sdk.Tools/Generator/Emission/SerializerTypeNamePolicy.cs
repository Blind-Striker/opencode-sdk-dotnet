using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class SerializerTypeNamePolicy
{
    /// <summary>Names the OpenCodeJsonContext accessor for a payload read.</summary>
    public static string ContextPropertyName(TypeReferencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsNullable)
        {
            throw new InvalidOperationException(
                $"No context accessor exists for a nullable '{plan.GetType().Name}' root; register it once a payload needs one.");
        }

        return plan switch
        {
            NamedTypeReferencePlan named => named.Name,
            ListTypeReferencePlan list => $"{ContextPropertyName(list.ElementType)}List",
            DictionaryTypeReferencePlan dictionary => $"{ContextPropertyName(dictionary.ValueType)}Dictionary",
            _ => throw new InvalidOperationException(
                $"No context accessor exists for plan '{plan.GetType().Name}'; register it once a payload needs one."),
        };
    }
}
