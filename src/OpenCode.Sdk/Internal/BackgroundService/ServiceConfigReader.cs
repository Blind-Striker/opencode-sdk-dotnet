using System.Text.Json;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The strict boundary over the CLI's service config (<c>ServiceConfig.Info</c> at the pin:
/// optional <c>hostname</c> string, <c>port</c> integer 1..65535, <c>password</c> string,
/// <c>cors</c> string array, <c>env</c> string map). The daemon persists its generated password
/// here, so the reader validates that member and never exposes it; only the environment map, the
/// one member the SDK consumes, comes out. An invalid document is absent config.
/// </summary>
internal static class ServiceConfigReader
{
    private const int MaxPort = 65_535;

    /// <summary>Validates a config document and extracts its environment map.</summary>
    /// <param name="utf8">The file's bytes.</param>
    /// <returns>The environment map (empty when the document declares none), or null for an invalid document.</returns>
    public static IReadOnlyDictionary<string, string>? TryReadEnvironment(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return Read(utf8);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string>? Read(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = Members.None;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var member = Identify(ref reader);
            if (member == Members.None)
            {
                reader.Skip();
                continue;
            }

            if ((seen & member) != Members.None || !ReadMember(ref reader, member, environment))
            {
                return null;
            }

            seen |= member;
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
        {
            return null;
        }

        return environment;
    }

    private static Members Identify(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("hostname"u8))
        {
            return Members.Hostname;
        }

        if (reader.ValueTextEquals("port"u8))
        {
            return Members.Port;
        }

        if (reader.ValueTextEquals("password"u8))
        {
            return Members.Password;
        }

        if (reader.ValueTextEquals("cors"u8))
        {
            return Members.Cors;
        }

        return reader.ValueTextEquals("env"u8) ? Members.Env : Members.None;
    }

    private static bool ReadMember(ref Utf8JsonReader reader, Members member, Dictionary<string, string> environment) =>
        member switch
        {
            // The password is accepted as the shape declares it; never retained, never returned.
            Members.Hostname or Members.Password => ReadString(ref reader),
            Members.Port => ReadPort(ref reader),
            Members.Cors => ReadStringArray(ref reader),
            Members.Env => ReadStringMap(ref reader, environment),
            Members.None => false,
            _ => false,
        };

    private static bool ReadString(ref Utf8JsonReader reader) =>
        reader.Read() && reader.TokenType == JsonTokenType.String;

    private static bool ReadPort(ref Utf8JsonReader reader) =>
        reader.Read() && reader.TokenType == JsonTokenType.Number &&
        reader.TryGetInt32(out var port) && port >= 1 && port <= MaxPort;

    private static bool ReadStringArray(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                return false;
            }
        }

        return reader.TokenType == JsonTokenType.EndArray;
    }

    private static bool ReadStringMap(ref Utf8JsonReader reader, Dictionary<string, string> target)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.String || target.ContainsKey(name))
            {
                return false;
            }

            target.Add(name, reader.GetString()!);
        }

        return reader.TokenType == JsonTokenType.EndObject;
    }

    [Flags]
    private enum Members
    {
        None = 0,
        Hostname = 1,
        Port = 2,
        Password = 4,
        Cors = 8,
        Env = 16,
    }
}
