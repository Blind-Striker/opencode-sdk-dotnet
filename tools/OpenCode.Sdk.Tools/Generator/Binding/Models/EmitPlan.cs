namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record EmitPlan
{
    public required IReadOnlyList<string> SelectedOperationIds
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<string>());

    public required IReadOnlyList<ModelPlan> Models
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<ModelPlan>());

    public required IReadOnlyList<UnionPlan> Unions
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<UnionPlan>());

    public required RegistryPlan Registry { get; init; }

    public required IReadOnlyList<ClientPlan> Clients
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<ClientPlan>());

    public required IReadOnlyList<PendingOperationPlan> PendingOperations
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<PendingOperationPlan>());

    /// <summary>
    /// Operations a <c>transportOwned</c> curation row covers: neither selected nor pending, their
    /// shape is fingerprint-pinned for a hand-written door (ADR-0021). Ordinal-sorted.
    /// </summary>
    public required IReadOnlyList<string> TransportOwnedOperationIds
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<string>());

    /// <summary>
    /// Gets the stabilize duplicates the binder folded mechanically, the committed telltale the
    /// generation manifest carries in place of the curated rows the convention retires.
    /// </summary>
    public required StabilizeDuplicateCollapse ImplicitAliases { get; init; }
}
