namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record RequestBodyPlan
{
    public required string TypeName { get; init; }

    public required string ParameterName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the operation parameter is optional; a body whose
    /// properties are all optional sends an empty JSON object when the caller passes nothing.
    /// </summary>
    public required bool IsOptional { get; init; }
}
