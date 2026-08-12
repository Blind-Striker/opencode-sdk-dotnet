namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record DictionaryTypeReferencePlan : TypeReferencePlan
{
    public required TypeReferencePlan ValueType { get; init; }

    public override bool IsCollection => true;
}
