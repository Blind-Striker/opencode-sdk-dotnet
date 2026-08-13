namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record OptionsPropertyPlan
{
    public required string WireName { get; init; }

    public required string PropertyName { get; init; }

    public required QueryValueKind Kind { get; init; }

    /// <summary>Gets a value indicating whether the property is inherited from the <c>ListOptions</c> base.</summary>
    public required bool IsInherited { get; init; }
}
