namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record ListTypeReferencePlan : TypeReferencePlan
{
    public required TypeReferencePlan ElementType { get; init; }

    public override bool IsCollection => true;
}
