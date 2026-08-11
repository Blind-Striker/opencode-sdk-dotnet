namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record ObjectModelPlan : ModelPlan
{
    public required IReadOnlyList<ModelPropertyPlan> Properties
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<ModelPropertyPlan>());

    public string? BaseTypeName { get; init; }
}
