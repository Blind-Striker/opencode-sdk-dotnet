namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record ClientReferencePlan
{
    public required string PropertyName { get; init; }

    public required string TypeName { get; init; }
}
