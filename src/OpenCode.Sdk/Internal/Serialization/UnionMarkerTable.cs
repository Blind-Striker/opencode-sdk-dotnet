using System.Collections.Frozen;

namespace OpenCode.Sdk.Internal.Serialization;

/// <summary>
/// One marker property of a union paired with the values its declared markers dispatch to.
/// A union whose branches were tagged by more than one wire dialect carries one table per
/// dialect, scanned in declaration order.
/// </summary>
/// <typeparam name="TValue">The value a known marker dispatches to.</typeparam>
internal readonly record struct UnionMarkerTable<TValue>
{
    public UnionMarkerTable(string propertyName, FrozenDictionary<string, TValue> types)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(types);

        PropertyName = propertyName;
        Types = types;
    }

    /// <summary>Gets the wire property carrying the marker.</summary>
    public string PropertyName { get; }

    /// <summary>Gets the values keyed by the markers declared under <see cref="PropertyName"/>.</summary>
    public FrozenDictionary<string, TValue> Types { get; }
}
