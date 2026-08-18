namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal abstract record TypeReferencePlan
{
    public required bool IsNullable { get; init; }

    public required JsonNullRepresentation JsonNullRepresentation { get; init; }

    public abstract bool IsCollection { get; }
}
