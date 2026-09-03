namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// The literal-tagged half of a marked union once bound: the dispatch entries, and the tags
/// whose schema admits no JSON value and therefore dispatch to nothing.
/// </summary>
internal sealed record UnionLiteralVariants(
    IReadOnlyList<UnionVariantPlan> Variants,
    IReadOnlyList<string> KnownImpossibleTags);
