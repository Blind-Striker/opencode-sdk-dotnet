namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record EnvelopePlan
{
    public required string ResponseTypeName { get; init; }

    public required string AdapterTypeName { get; init; }

    public required string PayloadName { get; init; }

    /// <summary>Gets the payload type name; for cursor lists, the element type of the <c>data</c> array.</summary>
    public required string PayloadTypeName { get; init; }

    public required EnvelopeKind Kind { get; init; }
}
