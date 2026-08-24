using System.Text.Json;
using OpenCode.Sdk.Internal.Serialization;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Reads a typed error body tolerantly for every response plane. Malformed JSON yields
/// <see langword="null"/> so the raw body remains the only record; an unknown tag keeps its
/// carrier; a known tag outside the operation's status map — or on an undeclared status,
/// when the allowed tags are <see langword="null"/> — downgrades to the unknown carrier so
/// the operation contract never widens.
/// </summary>
internal static class OpenCodeErrorReader
{
    public static IOpenCodeError? Read(string rawBody, string[]? allowedTags)
    {
        ArgumentNullException.ThrowIfNull(rawBody);

        try
        {
            var error = JsonSerializer.Deserialize(rawBody, OpenCodeJsonContext.Default.IOpenCodeError);
            return error switch
            {
                null => null,
                UnknownOpenCodeError unknown => unknown,
                // The tag sets are the generated per-status arrays, so the scan stays an
                // allocation-free ordinal lookup on this per-error hot path.
                _ when allowedTags is not null && Array.IndexOf(allowedTags, error.Tag) >= 0 => error,
                _ => Downgrade(error.Tag, rawBody),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Builds the failure an error status raises on the throwing channel.</summary>
    public static OpenCodeApiException CreateApiException(int status, IOpenCodeError? error, string? rawBody)
    {
        var statusText = status.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var message = error is null
            ? $"The opencode API returned status {statusText}."
            : $"The opencode API returned status {statusText} ('{error.Tag}').";

        return new OpenCodeApiException(message, status, error, rawBody);
    }

    private static UnknownOpenCodeError Downgrade(string tag, string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);
        return new UnknownOpenCodeError(tag, document.RootElement);
    }
}
