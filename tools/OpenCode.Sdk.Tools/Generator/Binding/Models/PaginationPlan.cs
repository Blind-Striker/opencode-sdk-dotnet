namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>Describes an automatic item traversal over an existing cursor-list operation.</summary>
internal sealed record PaginationPlan
{
    public required string MethodName { get; init; }

    public required string RequestTypeName { get; init; }

    public required string ItemTypeName { get; init; }

    public required string PayloadName { get; init; }
}
