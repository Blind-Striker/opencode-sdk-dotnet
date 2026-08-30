using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record UnionPlan
{
    /// <summary>Gets the emitted interface name, which is what every reference binds to.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the union name without its interface prefix, which names the members around it.</summary>
    public required string ConceptName { get; init; }

    public required string Namespace { get; init; }

    public required string UnknownTypeName { get; init; }

    public required string MarkerWireName { get; init; }

    public required string MarkerName { get; init; }

    public required LiteralKind MarkerKind { get; init; }

    /// <summary>
    /// Gets the further marker properties this union dispatches on, scanned after
    /// <see cref="MarkerWireName"/>. A union whose variants all share one marker leaves this empty.
    /// </summary>
    public IReadOnlyList<string> AlternateMarkerWireNames
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<string>());

    public required IReadOnlyList<UnionVariantPlan> Variants
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<UnionVariantPlan>());

    /// <summary>
    /// Gets declared marker values whose branch schemas admit no JSON value. They remain
    /// known protocol input and must be refused rather than routed to the unknown carrier.
    /// </summary>
    public IReadOnlyList<string> KnownImpossibleTags
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<string>());

    public string? Description { get; init; }

    /// <summary>Gets the outer union base type when this union is itself a nested variant.</summary>
    public string? BaseTypeName { get; init; }

    /// <summary>Gets the outer marker this nested union fixes to one value for all its variants.</summary>
    public UnionFixedMarkerPlan? FixedMarker { get; init; }
}
