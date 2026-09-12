namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// A record's explicit implementation of one hoisted interface member, emitted where the
/// record's own concretely typed property cannot satisfy the interface on its own — a value
/// type against a nullable member, or a promoted record against its hoisted carrier. The
/// record keeps its own property; an explicit implementation is not a public property, so
/// System.Text.Json never sees it.
/// </summary>
internal sealed record HoistedImplementationPlan
{
    /// <summary>Gets the interface that declares the member.</summary>
    public required string InterfaceName { get; init; }

    public required string MemberName { get; init; }

    /// <summary>Gets the type the declaring interface uses for the member.</summary>
    public required TypeReferencePlan MemberType { get; init; }

    /// <summary>Gets the record's own property the implementation returns.</summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets whether that property carries the tri-state <c>Optional&lt;T&gt;</c> wrapper, in which
    /// case the implementation returns its <c>Value</c>: the interface member is nullable because
    /// the union's unknown carrier materializes none of them, and an absent member reads as null
    /// there exactly as an explicit null does (ADR-0004, ADR-0011).
    /// </summary>
    public required bool ReadsOptionalValue { get; init; }
}
