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

    /// <summary>Gets every union interface this schema is a branch of; a schema can be a branch
    /// of more than one (ADR-0011).</summary>
    public IReadOnlyList<string> ImplementedUnionNames
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<string>());

    /// <summary>Gets the hoisted carriers this record implements beside its own identity.</summary>
    public IReadOnlyList<string> ImplementedHoistedInterfaceNames
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<string>());

    /// <summary>
    /// Gets the hoisted interface members this record answers explicitly because its own
    /// property cannot satisfy the declared type on its own.
    /// </summary>
    public IReadOnlyList<HoistedImplementationPlan> ExplicitHoistedImplementations
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<HoistedImplementationPlan>());

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
