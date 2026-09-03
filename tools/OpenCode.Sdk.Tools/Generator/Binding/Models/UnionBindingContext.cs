using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// What every branch of one marked union binds against: the union's identity, the marker it
/// dispatches on, and the accumulators that record membership and fixed outer markers.
/// </summary>
internal sealed record UnionBindingContext
{
    /// <summary>Gets the union's graph key — the subject of every refusal.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the union's bound interface name, which every member inherits.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the literal marker the union dispatches on.</summary>
    public required LiteralMarker Marker { get; init; }

    /// <summary>Gets the membership accumulator: a schema key to the unions it is a branch of.</summary>
    public required IDictionary<string, List<string>> Inheritance { get; init; }

    /// <summary>Gets the accumulator of outer markers a nested union fixes, by its member key.</summary>
    public required IDictionary<string, UnionFixedMarkerPlan> FixedMarkers { get; init; }

    public required BindingErrorCollector Errors { get; init; }
}
