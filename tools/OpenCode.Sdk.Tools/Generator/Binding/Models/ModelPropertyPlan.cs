using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record ModelPropertyPlan
{
    public required string WireName { get; init; }

    public required string Name { get; init; }

    public required TypeReferencePlan Type { get; init; }

    public required bool IsRequired { get; init; }

    /// <summary>Whether the wire contract admits an explicit JSON null, as opposed to the
    /// C# nullability an optional property gains for absence alone.</summary>
    public required bool AllowsWireNull { get; init; }

    public required bool IsLiteral { get; init; }

    public LiteralKind? LiteralKind { get; init; }

    public string? LiteralValue { get; init; }

    public string? Description { get; init; }
}
