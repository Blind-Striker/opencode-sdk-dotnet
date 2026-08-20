using System.Text;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>Re-encodes a UTF-8 wire body into the charset/BOM shapes the response decoder must match.</summary>
internal static class BodyEncodings
{
    private static readonly byte[] ReplacementTarget = "\"scenario\":\"deep-union-walk\""u8.ToArray();

    /// <summary>Prefixes the UTF-8 byte order mark.</summary>
    public static byte[] WithUtf8Bom(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return [.. Encoding.UTF8.GetPreamble(), .. body];
    }

    /// <summary>Transcodes to UTF-16 little-endian with its byte order mark, the BOM-selected fallback path.</summary>
    public static byte[] AsUtf16(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(body))];
    }

    /// <summary>
    /// Corrupts one byte inside a string value, so strict validation fails and the body takes
    /// the replacement-decoding path while remaining parseable JSON.
    /// </summary>
    public static byte[] WithMalformedUtf8(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var index = body.AsSpan().IndexOf(ReplacementTarget);
        if (index < 0)
        {
            throw new InvalidOperationException("The body no longer carries the string value the corruption targets.");
        }

        var corrupted = (byte[])body.Clone();
        corrupted[index + ReplacementTarget.Length - 2] = 0xFF;
        return corrupted;
    }
}
