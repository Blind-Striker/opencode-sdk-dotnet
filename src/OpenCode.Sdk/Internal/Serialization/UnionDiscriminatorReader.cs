using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace OpenCode.Sdk.Internal.Serialization;

/// <summary>Reads top-level union markers without materializing the known payload as a JSON DOM.</summary>
internal sealed class UnionDiscriminatorReader
{
#if NET9_0_OR_GREATER
    /// <summary>Bounds the stack copy used for allocation-free dispatch; every declared tag is
    /// far shorter, so a longer marker is necessarily unknown and may take the string path.</summary>
    private const int MaxStackMarkerLength = 128;
#endif

    private readonly JsonTokenType _objectToken = JsonTokenType.StartObject;

    /// <summary>
    /// Dispatches a string marker against the tag table: a known tag returns its mapped value
    /// without materializing the marker string (on runtimes with alternate span lookup), while
    /// an unknown tag materializes the marker for the caller's carrier.
    /// </summary>
    public bool TryFindKnown<TValue>(
        ref Utf8JsonReader reader,
        string propertyName,
        string conceptName,
        FrozenDictionary<string, TValue> typesByTag,
        [MaybeNullWhen(false)] out TValue type,
        [NotNullWhen(false)] out string? marker)
    {
        ArgumentNullException.ThrowIfNull(typesByTag);

        var markerReader = Find(ref reader, propertyName, conceptName);
        return Dispatch(ref markerReader, propertyName, typesByTag, out type, out marker);
    }

    /// <summary>
    /// The multi-dialect twin of <see cref="TryFindKnown{TValue}(ref Utf8JsonReader, string, string, FrozenDictionary{string, TValue}, out TValue, out string)"/>:
    /// the first declared marker property the payload carries decides the dispatch, so a union
    /// tagged by two wire dialects reads either without a second pass or a JSON DOM. A payload
    /// carrying none of them is malformed under this union's contract.
    /// </summary>
    public bool TryFindKnown<TValue>(
        ref Utf8JsonReader reader,
        string conceptName,
        UnionMarkerTable<TValue>[] markerTables,
        [MaybeNullWhen(false)] out TValue type,
        [NotNullWhen(false)] out string? marker)
    {
        ArgumentNullException.ThrowIfNull(markerTables);

        foreach (var table in markerTables)
        {
            if (!TryFind(ref reader, table.PropertyName, conceptName, out var candidate))
            {
                continue;
            }

            return Dispatch(ref candidate, table.PropertyName, table.Types, out type, out marker);
        }

        throw new JsonException($"The {conceptName} payload must contain {DescribeMarkers(markerTables)}.");
    }

    public bool ReadBoolean(ref Utf8JsonReader reader, string propertyName, string conceptName)
    {
        var marker = Find(ref reader, propertyName, conceptName);
        if (marker.TokenType is not (JsonTokenType.True or JsonTokenType.False))
        {
            throw new JsonException($"The '{propertyName}' marker must be a boolean.");
        }

        return marker.GetBoolean();
    }

    public void RequireString(ref Utf8JsonReader reader, string propertyName, string expected, string conceptName)
    {
        var marker = Find(ref reader, propertyName, conceptName);
        if (marker.TokenType is not JsonTokenType.String || !marker.ValueTextEquals(expected))
        {
            throw new JsonException($"The '{propertyName}' marker must be '{expected}'.");
        }
    }

    public void RequireBoolean(ref Utf8JsonReader reader, string propertyName, bool expected, string conceptName)
    {
        var marker = Find(ref reader, propertyName, conceptName);
        if (marker.TokenType != (expected ? JsonTokenType.True : JsonTokenType.False))
        {
            throw new JsonException($"The '{propertyName}' marker must be '{expected}'.");
        }
    }

    private static string DescribeMarkers<TValue>(UnionMarkerTable<TValue>[] markerTables)
    {
        var names = new string[markerTables.Length];
        for (var index = 0; index < markerTables.Length; index++)
        {
            names[index] = $"'{markerTables[index].PropertyName}'";
        }

        return string.Join(" or ", names);
    }

    private static bool Dispatch<TValue>(
        ref Utf8JsonReader markerReader,
        string propertyName,
        FrozenDictionary<string, TValue> typesByTag,
        [MaybeNullWhen(false)] out TValue type,
        [NotNullWhen(false)] out string? marker)
    {
        if (markerReader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"The '{propertyName}' marker must be a string.");
        }

#if NET9_0_OR_GREATER
        // UTF-8 bytes never decode to more chars than bytes, so the byte length bounds the copy.
        var utf8Length = markerReader.HasValueSequence
            ? checked((int)markerReader.ValueSequence.Length)
            : markerReader.ValueSpan.Length;
        if (utf8Length <= MaxStackMarkerLength)
        {
            Span<char> buffer = stackalloc char[MaxStackMarkerLength];
            var span = buffer[..markerReader.CopyString(buffer)];
            if (span.IsWhiteSpace())
            {
                throw new JsonException($"The '{propertyName}' marker must be a non-empty string.");
            }

            if (typesByTag.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(span, out type!))
            {
                marker = null;
                return true;
            }

            marker = new string(span);
            return false;
        }
#endif

        if (markerReader.GetString() is not { } value || string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"The '{propertyName}' marker must be a non-empty string.");
        }

        if (typesByTag.TryGetValue(value, out type!))
        {
            marker = null;
            return true;
        }

        marker = value;
        return false;
    }

    private Utf8JsonReader Find(ref Utf8JsonReader reader, string propertyName, string conceptName) =>
        TryFind(ref reader, propertyName, conceptName, out var marker)
            ? marker
            : throw new JsonException($"The {conceptName} payload must contain '{propertyName}'.");

    /// <summary>
    /// Scans for one marker property without judging its absence: a union dispatching on more
    /// than one dialect asks for each in turn, and only the last absence is a malformed payload.
    /// A payload that is not a well-formed object still throws here, on the first ask.
    /// </summary>
    private bool TryFind(ref Utf8JsonReader reader, string propertyName, string conceptName, out Utf8JsonReader value)
    {
        if (reader.TokenType != _objectToken)
        {
            throw new JsonException($"The {conceptName} payload must be a JSON object.");
        }

        var scan = reader;
        var found = false;
        var marker = default(Utf8JsonReader);
        while (scan.Read() && scan.TokenType is not JsonTokenType.EndObject)
        {
            if (scan.TokenType is not JsonTokenType.PropertyName)
            {
                throw new JsonException($"The {conceptName} payload must be a JSON object.");
            }

            var matches = scan.ValueTextEquals(propertyName);
            if (!scan.Read())
            {
                throw new JsonException($"The {conceptName} payload ended before '{propertyName}'.");
            }

            if (matches)
            {
                marker = scan;
                found = true;
            }

            if (!scan.TrySkip())
            {
                throw new JsonException($"The {conceptName} payload ended before '{propertyName}'.");
            }
        }

        value = marker;
        return found;
    }
}
