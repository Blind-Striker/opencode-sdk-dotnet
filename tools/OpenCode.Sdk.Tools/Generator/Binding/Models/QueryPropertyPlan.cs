namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record QueryPropertyPlan
{
    public required string WireName { get; init; }

    public required string PropertyName { get; init; }

    public required QueryValueKind Kind { get; init; }

    /// <summary>Gets a value indicating whether the property is inherited from the <c>ListRequest</c> base.</summary>
    public required bool IsInherited { get; init; }
}
