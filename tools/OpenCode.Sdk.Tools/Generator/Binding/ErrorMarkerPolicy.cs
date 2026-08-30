using System.Diagnostics.CodeAnalysis;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Owns the one rule that maps an error dialect to the wire property carrying its tag, so the
/// error-union binder, the per-status error map, and the emitted converter never spell the
/// marker names themselves. The order is the order the generated converter scans the payload:
/// an Effect <c>_tag</c> first, then the <c>{name, data}</c> dialect. A dialect absent from this
/// table has no marker and is refused by name (ADR-0007 admits tagged payloads, not untagged ones).
/// </summary>
internal static class ErrorMarkerPolicy
{
    private static readonly (ErrorStyle Style, string WireName)[] Dialects =
    [
        (ErrorStyle.EffectTag, "_tag"),
        (ErrorStyle.NameData, "name"),
    ];

    /// <summary>Gets every admitted marker property, in the order a payload is scanned for one.</summary>
    public static IReadOnlyList<string> ScanOrder { get; } =
        Array.AsReadOnly([.. Dialects.Select(static dialect => dialect.WireName)]);

    public static bool TryGetWireName(ErrorStyle style, [NotNullWhen(true)] out string? wireName)
    {
        foreach (var (candidate, name) in Dialects)
        {
            if (candidate == style)
            {
                wireName = name;
                return true;
            }
        }

        wireName = null;
        return false;
    }

    /// <summary>
    /// Resolves the single literal marker an error schema dispatches on. A schema whose dialect
    /// has no marker property, or that does not carry exactly one required literal under it,
    /// resolves to <see langword="null"/> and states why.
    /// </summary>
    public static LiteralMarker? Resolve(ObjectNode node, out string problem)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!TryGetWireName(node.ErrorStyle, out var wireName))
        {
            problem = $"error style '{node.ErrorStyle}' declares no tag marker property";
            return null;
        }

        var markers = node.LiteralMarkers.Where(marker => string.Equals(marker.PropertyName, wireName, StringComparison.Ordinal)).ToArray();
        if (markers is not [var marker])
        {
            problem = $"a tagged error must declare exactly one required '{wireName}' literal";
            return null;
        }

        problem = string.Empty;
        return marker;
    }
}
