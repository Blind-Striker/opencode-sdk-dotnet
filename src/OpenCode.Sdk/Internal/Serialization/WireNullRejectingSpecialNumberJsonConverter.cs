using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Internal.Serialization;

/// <summary>
/// Preserves the number-or-named-value wire contract while rejecting explicit null on an
/// optional property whose schema permits omission but not null.
/// </summary>
internal sealed class WireNullRejectingSpecialNumberJsonConverter : JsonConverter<double?>
{
    /// <summary>Null must reach <see cref="Read"/>; the serializer would otherwise assign it silently.</summary>
    public override bool HandleNull => true;

    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            throw new JsonException("The property does not admit null; the server must omit it instead.");
        }

        if (reader.TokenType is JsonTokenType.Number)
        {
            return reader.GetDouble();
        }

        if (reader.TokenType is JsonTokenType.String)
        {
            if (reader.ValueTextEquals("NaN"))
            {
                return double.NaN;
            }

            if (reader.ValueTextEquals("Infinity"))
            {
                return double.PositiveInfinity;
            }

            if (reader.ValueTextEquals("-Infinity"))
            {
                return double.NegativeInfinity;
            }
        }

        throw new JsonException("Expected a number or one of the named values 'NaN', 'Infinity', or '-Infinity'.");
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (double.IsNaN(value.Value))
        {
            writer.WriteStringValue("NaN");
        }
        else if (double.IsPositiveInfinity(value.Value))
        {
            writer.WriteStringValue("Infinity");
        }
        else if (double.IsNegativeInfinity(value.Value))
        {
            writer.WriteStringValue("-Infinity");
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }
}
