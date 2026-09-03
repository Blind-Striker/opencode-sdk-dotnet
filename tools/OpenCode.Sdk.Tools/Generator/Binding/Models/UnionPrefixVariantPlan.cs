namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>The at-most-one arm of a marked union whose tag is a literal prefix rather than a literal.</summary>
internal sealed record UnionPrefixVariantPlan
{
    public required string TypeName { get; init; }

    /// <summary>Gets the decoded literal prefix every value of the marker starts with.</summary>
    public required string Prefix { get; init; }

    /// <summary>Gets the wire property carrying the prefix; always the union's own marker.</summary>
    public required string MarkerWireName { get; init; }
}
