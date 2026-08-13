namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record OperationParameterPlan
{
    public required string WireName { get; init; }

    public required string Name { get; init; }

    public required string TypeName { get; init; }

    /// <summary>Gets a value indicating whether the bound handle supplies this parameter.</summary>
    public required bool IsHandleParameter { get; init; }
}
