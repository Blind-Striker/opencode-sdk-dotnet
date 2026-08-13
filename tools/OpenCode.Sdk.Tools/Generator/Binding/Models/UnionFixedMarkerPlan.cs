using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record UnionFixedMarkerPlan
{
    public required string WireName { get; init; }

    public required string Name { get; init; }

    public required LiteralKind Kind { get; init; }

    public required string Value { get; init; }
}
