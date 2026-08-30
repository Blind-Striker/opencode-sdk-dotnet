using System.Text.Json;

namespace OpenCode.Sdk.Internal.Serialization;

/// <summary>Prevents a hand-constructed unknown carrier from replaying a payload with different markers.</summary>
internal sealed class UnionPayloadGuard
{
    private readonly JsonValueKind _falseToken = JsonValueKind.False;
    private readonly JsonValueKind _stringToken = JsonValueKind.String;
    private readonly JsonValueKind _trueToken = JsonValueKind.True;

    public static UnionPayloadGuard Instance { get; } = new();

    private UnionPayloadGuard()
    {
    }

    public void RequireString(JsonElement payload, string propertyName, string expected)
    {
        if (payload.ValueKind is not JsonValueKind.Object
            || !payload.TryGetProperty(propertyName, out var marker)
            || marker.ValueKind != _stringToken
            || !marker.ValueEquals(expected))
        {
            throw Mismatch(propertyName, nameof(payload));
        }
    }

    /// <summary>
    /// The multi-dialect twin of <see cref="RequireString"/>: the carrier of a union tagged by
    /// two wire dialects agrees with the payload when any one of the declared marker properties
    /// carries the constructor marker, and the payload names the dialect it used.
    /// </summary>
    public void RequireStringAmong(JsonElement payload, string[] propertyNames, string expected)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);

        if (payload.ValueKind is JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (payload.TryGetProperty(propertyName, out var marker)
                    && marker.ValueKind == _stringToken
                    && marker.ValueEquals(expected))
                {
                    return;
                }
            }
        }

        throw MismatchAmong(propertyNames, nameof(payload));
    }

    public void RequireBoolean(JsonElement payload, string propertyName, bool expected)
    {
        if (payload.ValueKind is not JsonValueKind.Object
            || !payload.TryGetProperty(propertyName, out var marker)
            || marker.ValueKind != (expected ? _trueToken : _falseToken))
        {
            throw Mismatch(propertyName, nameof(payload));
        }
    }

    private static ArgumentException Mismatch(string propertyName, string parameterName) =>
        MismatchAmong([propertyName], parameterName);

    /// <summary>
    /// Names every marker a payload could have carried. The quoting lives here rather than in the
    /// caller's join: a caller splicing quote characters into the value this method then quotes
    /// again produces the right text only by accident of the format string's shape.
    /// </summary>
    private static ArgumentException MismatchAmong(IReadOnlyList<string> propertyNames, string parameterName) =>
        new(
            "The payload must contain a "
            + string.Join(" or ", propertyNames.Select(static name => $"'{name}'"))
            + " marker that agrees with the constructor marker.",
            parameterName);
}
