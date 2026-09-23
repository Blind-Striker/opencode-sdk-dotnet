using System.Text.Json;

namespace OpenCode.Sdk.Internal.BackgroundService.Registration;

/// <summary>
/// The one decode step the files the CLI writes go through: a UTF-8 byte-order mark is skipped, as
/// the CLI's own decoder skips it (a hand-edited service config may carry one), a repeated member
/// is refused rather than resolved last-wins, and every document that is not JSON, or holds a
/// string that is not valid UTF-8, is absent state rather than a failure.
/// </summary>
internal static class StrictJson
{
    private static readonly JsonDocumentOptions Options = new() { AllowDuplicateProperties = false };

    /// <summary>Parses a document and reads it through the shape the caller owns.</summary>
    /// <typeparam name="T">What the caller reads out.</typeparam>
    /// <param name="utf8">The file's bytes.</param>
    /// <param name="read">The caller's shape; it returns null for a document it does not accept.</param>
    /// <returns>What the shape read, or null for a document it did not accept or could not decode.</returns>
    public static T? TryRead<T>(ReadOnlySpan<byte> utf8, Func<JsonElement, T?> read)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(read);

        if (utf8.StartsWith("﻿"u8))
        {
            utf8 = utf8["﻿"u8.Length..];
        }

        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray(), Options);
            return read(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // Not JSON, a repeated member, or a string that is not valid UTF-8 (which JsonDocument
            // reports only when the shape decodes it): not a document the CLI wrote either way.
            return null;
        }
    }

    /// <summary>Reads an optional string member: absent is null, a string is its value, anything else refuses the document.</summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The member name.</param>
    /// <param name="value">The value, or null when the member is absent.</param>
    /// <returns>False when the member is present but not a string.</returns>
    public static bool TryGetOptionalString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var member))
        {
            return true;
        }

        if (member.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = member.GetString();
        return true;
    }
}
