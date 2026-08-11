namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal abstract record ModelPlan
{
    public required string Name { get; init; }

    public required string Namespace { get; init; }

    public string? Description { get; init; }
}
