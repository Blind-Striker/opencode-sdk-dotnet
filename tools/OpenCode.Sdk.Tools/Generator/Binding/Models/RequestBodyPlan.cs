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

    /// <summary>
    /// Gets a value indicating whether the body is raw bytes the caller supplies as a
    /// <see cref="Stream"/> (<c>application/octet-stream</c>, <c>string</c>/<c>binary</c>):
    /// it is sent unbuffered, never serialized, and never absorbs the query parameters.
    /// </summary>
    public bool IsBinary { get; init; }
}
