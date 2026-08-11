using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record UnionPlan
{
    public required string Name { get; init; }

    public required string Namespace { get; init; }

    public required string UnknownTypeName { get; init; }

    public required string MarkerWireName { get; init; }

    public required string MarkerName { get; init; }

    public required LiteralKind MarkerKind { get; init; }

    public required IReadOnlyList<UnionVariantPlan> Variants
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<UnionVariantPlan>());

    public string? Description { get; init; }
}
