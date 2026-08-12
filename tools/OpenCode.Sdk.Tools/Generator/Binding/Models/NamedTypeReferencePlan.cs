namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record NamedTypeReferencePlan : TypeReferencePlan
{
    public required string Name { get; init; }

    public override bool IsCollection => false;
}
