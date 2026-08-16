namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record ObjectModelPlan : ModelPlan
{
    public required IReadOnlyList<ModelPropertyPlan> Properties
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<ModelPropertyPlan>());

    public string? BaseTypeName { get; init; }

    /// <summary>
    /// Gets the query-side properties a merged operation request carries beside its body
    /// properties; they never serialize and the route builder consumes them instead.
    /// </summary>
    public IReadOnlyList<QueryPropertyPlan> RequestQueryProperties
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<QueryPropertyPlan>());
}
