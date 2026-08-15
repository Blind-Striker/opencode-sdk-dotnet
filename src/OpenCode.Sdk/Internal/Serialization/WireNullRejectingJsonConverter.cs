using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenCode.Sdk.Internal.Serialization;

/// <summary>
/// Rejects an explicit JSON null on an optional reference-typed property whose schema does not
/// admit null; absence never invokes a property converter, so omission stays legal.
/// </summary>
/// <typeparam name="T">The property's non-nullable reference type.</typeparam>
internal sealed class WireNullRejectingJsonConverter<T> : JsonConverter<T>
    where T : class
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

        JsonSerializer.Serialize(writer, value, ResolveInner(options));
    }

    /// <summary>The property attribute never replaces the type's own metadata, so this cannot recurse.</summary>
    private static JsonTypeInfo<T> ResolveInner(JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
