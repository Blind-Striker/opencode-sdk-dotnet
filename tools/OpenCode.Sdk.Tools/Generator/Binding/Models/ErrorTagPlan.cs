namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record ErrorTagPlan
{
    public required string Tag { get; init; }

    public required string TypeName { get; init; }
}
