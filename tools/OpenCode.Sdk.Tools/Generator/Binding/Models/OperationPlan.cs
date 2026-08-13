namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record OperationPlan
{
    public required string MethodName { get; init; }

    /// <summary>Gets the invariant-lowercase HTTP method.</summary>
    public required string HttpMethod { get; init; }

    public required string RouteTemplate { get; init; }

    /// <summary>Gets the nested container the route members live in (client name, or the group for root placement).</summary>
    public required string RouteContainerName { get; init; }

    /// <summary>Gets the route member name inside its container; containers never restate the group in members.</summary>
    public required string RouteMemberName { get; init; }

    /// <summary>Gets every route parameter in template order, including the handle-supplied one.</summary>
    public required IReadOnlyList<OperationParameterPlan> Parameters
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<OperationParameterPlan>());

    /// <summary>Gets the query-options record plan, or <see langword="null"/> when the operation has no query parameters.</summary>
    public OperationOptionsPlan? Options { get; init; }

    public required EnvelopePlan Envelope { get; init; }

    public required ErrorMapPlan ErrorMap { get; init; }

    public string? Summary { get; init; }

    public string? Description { get; init; }
}
