using System.Text.Json;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record StructuralUnionArmPlan
{
    public required string Name { get; init; }

    public required TypeReferencePlan Type { get; init; }

    public required IReadOnlyList<JsonTokenType> Tokens
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<JsonTokenType>());
}
