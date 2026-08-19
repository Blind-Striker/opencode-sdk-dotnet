namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record StructuralUnionModelPlan : ModelPlan
{
    public required string KindTypeName { get; init; }

    public required IReadOnlyList<StructuralUnionArmPlan> Arms
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<StructuralUnionArmPlan>());
}
