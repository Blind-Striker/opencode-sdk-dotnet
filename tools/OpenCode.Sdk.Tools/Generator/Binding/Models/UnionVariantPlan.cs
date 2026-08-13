namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record UnionVariantPlan
{
    public required string TypeName { get; init; }

    public required string Tag { get; init; }

    /// <summary>Gets whether the variant is itself a nested marked union base.</summary>
    public bool IsNestedUnion { get; init; }
}
