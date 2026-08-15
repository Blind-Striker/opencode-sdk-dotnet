namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>A curated pair of query parameters the route builder refuses to combine.</summary>
internal sealed record ExclusiveQueryPairPlan
{
    public required string FirstWireName { get; init; }

    public required string SecondWireName { get; init; }
}
