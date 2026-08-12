using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record PendingOperationPlan
{
    public required string OperationId { get; init; }

    public required SpecSurface Surface { get; init; }
}
