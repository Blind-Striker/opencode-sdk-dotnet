namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record EnumValuePlan
{
    public required string Name { get; init; }

    public required string WireValue { get; init; }
}
