using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenCode.Sdk.Internal.Serialization;

/// <summary>
/// Rejects an explicit JSON null on an optional value-typed property whose schema does not
/// admit null; the converter targets the nullable shape because the serializer's nullable
/// wrapper would otherwise consume the null before any inner converter runs.
/// </summary>
/// <typeparam name="T">The property's underlying value type.</typeparam>
internal sealed class WireNullRejectingValueJsonConverter<T> : JsonConverter<T?>
    where T : struct
{
    /// <summary>Null must reach <see cref="Read"/>; the serializer would otherwise assign it silently.</summary>
    public override bool HandleNull => true;

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            throw new JsonException("The property does not admit null; the server must omit it instead.");
        }

        return JsonSerializer.Deserialize(ref reader, ResolveInner(options));
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Value, ResolveInner(options));
    }

    /// <summary>The property attribute never replaces the type's own metadata, so this cannot recurse.</summary>
    private static JsonTypeInfo<T> ResolveInner(JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
