namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record EnvelopePlan
{
    public required string ResponseTypeName { get; init; }

    public required string AdapterTypeName { get; init; }

    public required string PayloadName { get; init; }

    public required string PayloadTypeName { get; init; }

    /// <summary>Gets a value indicating whether the wire success body wraps the payload in a <c>data</c> property.</summary>
    public required bool HasDataEnvelope { get; init; }
}
