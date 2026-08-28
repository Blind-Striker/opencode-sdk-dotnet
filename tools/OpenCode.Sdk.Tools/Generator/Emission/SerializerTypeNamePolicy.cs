using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class SerializerTypeNamePolicy
{
    /// <summary>Names the OpenCodeJsonContext accessor for a payload read.</summary>
    public static string ContextPropertyName(TypeReferencePlan plan) => plan switch
    {
        NamedTypeReferencePlan named => named.Name,
        _ => throw new InvalidOperationException(
            $"No context accessor exists for plan '{plan.GetType().Name}'; register it in Task 3."),
    };
}
