using System.Text.Json;

namespace OpenCode.Sdk.Internal.Serialization;

/// <summary>Reads top-level union markers without materializing the known payload as a JSON DOM.</summary>
internal sealed class UnionDiscriminatorReader
{
    private readonly JsonTokenType _objectToken = JsonTokenType.StartObject;

    public string ReadString(ref Utf8JsonReader reader, string propertyName, string conceptName)
    {
        var marker = Find(ref reader, propertyName, conceptName);
        if (marker.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"The '{propertyName}' marker must be a string.");
        }

        if (marker.GetString() is not { } value || string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"The '{propertyName}' marker must be a non-empty string.");
        }

        return value;
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

    private Utf8JsonReader Find(ref Utf8JsonReader reader, string propertyName, string conceptName)
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

        if (!found)
        {
            throw new JsonException($"The {conceptName} payload must contain '{propertyName}'.");
        }

        return marker;
    }
}
