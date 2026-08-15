namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record QueryRequestPlan
{
    public required string TypeName { get; init; }

    /// <summary>Gets a value indicating whether the record derives from the <c>ListRequest</c> base.</summary>
    public required bool DerivesFromListRequest { get; init; }

    /// <summary>Gets every bound query parameter in wire order, including base-inherited ones.</summary>
    public required IReadOnlyList<QueryPropertyPlan> Properties
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<QueryPropertyPlan>());
}
