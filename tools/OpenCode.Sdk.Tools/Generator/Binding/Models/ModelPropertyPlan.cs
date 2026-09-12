using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record ModelPropertyPlan
{
    public required string WireName { get; init; }

    public required string Name { get; init; }

    public required TypeReferencePlan Type { get; init; }

    public required bool IsRequired { get; init; }

    public required bool IsLiteral { get; init; }

    /// <summary>
    /// Gets whether the property emits the tri-state <c>Optional&lt;T&gt;</c> wrapper: its schema is
    /// reachable from a selected request body, the document declares the property optional, and the
    /// document admits JSON null for it. The flag is read before optional-ness widens
    /// <see cref="TypeReferencePlan.IsNullable" />, which is the only place the two are still
    /// distinguishable (ADR-0004).
    /// </summary>
    public required bool EmitsOptionalWrapper { get; init; }

    public LiteralKind? LiteralKind { get; init; }

    public string? LiteralValue { get; init; }

    public string? Description { get; init; }
}
