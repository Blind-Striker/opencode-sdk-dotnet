using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class SourceEmitter
{
    public static IReadOnlyList<GeneratedSource> Emit(EmitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var sources = new List<GeneratedSource>();
        sources.AddRange(ModelEmitter.Emit(plan));
        sources.AddRange(UnionEmitter.Emit(plan.Unions));
        sources.AddRange(RegistryEmitter.Emit(plan.Registry));
        sources.Add(OptionalCollectionInputEmitter.Emit());

        var duplicate = sources
            .GroupBy(static source => source.RelativePath, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Skip(1).Any());

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Multiple emitters produced '{duplicate.Key}'.");
        }

        return Array.AsReadOnly([.. sources.OrderBy(static source => source.RelativePath, StringComparer.Ordinal)]);
    }
}
