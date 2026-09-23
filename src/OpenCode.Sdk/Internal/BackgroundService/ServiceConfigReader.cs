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
    public static IReadOnlyDictionary<string, string>? TryReadEnvironment(ReadOnlySpan<byte> utf8) =>
        StrictJson.TryRead(utf8, Read);

    private static Dictionary<string, string>? Read(JsonElement root)
    {
        // The password is accepted as the shape declares it; never retained, never returned.
        if (root.ValueKind != JsonValueKind.Object
            || !StrictJson.TryGetOptionalString(root, "hostname", out _)
            || !StrictJson.TryGetOptionalString(root, "password", out _)
            || !IsValidPort(root)
            || !IsValidCors(root))
        {
            return null;
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("env", out var env))
        {
            return environment;
        }

        if (env.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var entry in env.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            environment.Add(entry.Name, entry.Value.GetString()!);
        }

        return environment;
    }

    private static bool IsValidPort(JsonElement root) =>
        !root.TryGetProperty("port", out var port) ||
        (port.ValueKind == JsonValueKind.Number && port.TryGetInt32(out var value) && value >= 1 && value <= MaxPort);

    private static bool IsValidCors(JsonElement root) =>
        !root.TryGetProperty("cors", out var cors) ||
        (cors.ValueKind == JsonValueKind.Array && cors.EnumerateArray().All(static origin => origin.ValueKind == JsonValueKind.String));
}
