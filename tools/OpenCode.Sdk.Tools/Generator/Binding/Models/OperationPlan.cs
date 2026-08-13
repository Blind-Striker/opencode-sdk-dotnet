namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record OperationPlan
{
    public required string MethodName { get; init; }

    /// <summary>Gets the invariant-lowercase HTTP method.</summary>
    public required string HttpMethod { get; init; }

    public required string RouteTemplate { get; init; }

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

    public required EnvelopePlan Envelope { get; init; }

    public required ErrorMapPlan ErrorMap { get; init; }

    public string? Summary { get; init; }

    public string? Description { get; init; }
}
