using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenCode.Sdk.Internal.Serialization;

/// <summary>
/// Deserializes a cursor-list page whose wire contract admits neither a null array nor null
/// elements; either shape is a malformed success the adapters turn into a transport failure.
/// </summary>
/// <typeparam name="T">The page's non-nullable element type.</typeparam>
internal sealed class NullElementRejectingListJsonConverter<T> : JsonConverter<IReadOnlyList<T>>
    where T : class
{
    /// <summary>Null must reach <see cref="Read"/>; the serializer would otherwise assign it silently.</summary>
    public override bool HandleNull => true;

    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.StartArray)
        {
            throw new JsonException("The page payload must be a JSON array.");
        }

        var elementTypeInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        var items = new List<T>();
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndArray)
            {
                return items;
            }

            if (reader.TokenType is JsonTokenType.Null)
            {
                throw new JsonException("The page payload does not admit null elements.");
            }

            items.Add(JsonSerializer.Deserialize(ref reader, elementTypeInfo)
                      ?? throw new JsonException("The page payload does not admit null elements."));
        }

        throw new JsonException("The page payload array was truncated.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        var elementTypeInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, elementTypeInfo);
        }

        writer.WriteEndArray();
    }
}
