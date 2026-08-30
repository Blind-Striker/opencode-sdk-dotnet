namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record UnionVariantPlan
{
    public required string TypeName { get; init; }

    public required string Tag { get; init; }

    /// <summary>Gets the wire property carrying this variant's tag; a union may dispatch on more than one.</summary>
    public required string MarkerWireName { get; init; }

    /// <summary>Gets whether the variant is itself a nested marked union base.</summary>
    public bool IsNestedUnion { get; init; }
}
