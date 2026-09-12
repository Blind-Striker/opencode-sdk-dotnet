using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Names the records a union's dispatch can actually produce. A nested-union variant expands to
/// its own leaves, because those are the records that must satisfy whatever the outer interface
/// promises; a known-impossible tag contributes nothing, since it has no variant to satisfy it
/// (ADR-0015).
/// </summary>
internal static class UnionMemberCollector
{
    public static IReadOnlyList<string> Collect(UnionPlan union, IReadOnlyDictionary<string, UnionPlan> unions)
    {
        ArgumentNullException.ThrowIfNull(union);
        ArgumentNullException.ThrowIfNull(unions);

        var result = new List<string>(union.Variants.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Walk(union, unions, result, visited);
        return Array.AsReadOnly([.. result]);
    }

    private static void Walk(UnionPlan union, IReadOnlyDictionary<string, UnionPlan> unions, List<string> result,
        HashSet<string> visited)
    {
        if (!visited.Add(union.Name))
        {
            return;
        }

        foreach (var variant in union.Variants)
        {
            if (variant.IsNestedUnion && unions.TryGetValue(variant.TypeName, out var nested))
            {
                Walk(nested, unions, result, visited);
                continue;
            }

            Add(result, variant.TypeName);
        }

        if (union.PrefixVariant is { } prefix)
        {
            Add(result, prefix.TypeName);
        }
    }

    private static void Add(List<string> result, string typeName)
    {
        if (!result.Contains(typeName, StringComparer.Ordinal))
        {
            result.Add(typeName);
        }
    }
}
