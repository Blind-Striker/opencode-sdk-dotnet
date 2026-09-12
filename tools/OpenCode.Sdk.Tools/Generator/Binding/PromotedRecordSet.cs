using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Names the distinct records a property promotes across a set of members. Upstream inlines one
/// struct per definition, so the members each carry their own record for the same shape; a
/// property that names one type for everybody is not that case and needs no carrier.
/// </summary>
internal static class PromotedRecordSet
{
    public static IReadOnlyList<string>? Resolve(IEnumerable<ModelPropertyPlan> properties,
        IReadOnlyDictionary<string, ObjectModelPlan> models)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(models);

        var promoted = new List<string>();
        foreach (var property in properties)
        {
            if (property.Type is not NamedTypeReferencePlan named || !models.ContainsKey(named.Name))
            {
                return null;
            }

            if (!promoted.Contains(named.Name, StringComparer.Ordinal))
            {
                promoted.Add(named.Name);
            }
        }

        return promoted.Count > 1 ? Array.AsReadOnly([.. promoted]) : null;
    }
}
