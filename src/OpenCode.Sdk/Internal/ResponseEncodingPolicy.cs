using System.Text;

namespace OpenCode.Sdk.Internal;

/// <summary>Matches HttpContent string decoding while retaining valid UTF-8 for direct JSON materialization.</summary>
internal sealed class ResponseEncodingPolicy
{
    private const int BigEndianUnicodeCodePage = 1201;
    private const int UnicodeCodePage = 1200;
    private const int Utf8CodePage = 65001;
    private const int Utf32CodePage = 12000;

    private readonly Encoding _strictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public EncodedResponseBody Decode(byte[] body, string? charset)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Length is 0)
        {
            // HttpContent returns an empty string before consulting an invalid charset.
            return new EncodedResponseBody(body, DecodedBody: null);
        }

        var encoding = ResolveEncoding(body, charset, out var preambleLength);
        if (encoding.CodePage == Utf8CodePage && IsValidUtf8(body, preambleLength))
        {
            return new EncodedResponseBody(body.AsMemory(preambleLength), DecodedBody: null);
        }

        return new EncodedResponseBody(
            Utf8Body: default,
            encoding.GetString(body, preambleLength, body.Length - preambleLength));
    }

    private bool IsValidUtf8(byte[] body, int offset)
    {
        try
        {
            _ = _strictUtf8.GetCharCount(body, offset, body.Length - offset);
            return true;
        }
        catch (DecoderFallbackException)
        {
            // ReadAsStringAsync replacement-decodes malformed UTF-8. Preserve that path.
            return false;
        }
    }

    private static Encoding ResolveEncoding(byte[] body, string? charset, out int preambleLength)
    {
        if (charset is not null)
        {
            try
            {
                var unquoted = charset.Length > 2 && charset[0] is '"' && charset[^1] is '"'
                    ? charset[1..^1]
                    : charset;
                var declared = Encoding.GetEncoding(unquoted);
                preambleLength = GetPreambleLength(body, declared);
                return declared;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                throw new InvalidOperationException("The response content declared an invalid charset.", exception);
            }
        }

        if (StartsWith(body, 0xEF, 0xBB, 0xBF))
        {
            preambleLength = 3;
            return Encoding.UTF8;
        }

        if (StartsWith(body, 0xFF, 0xFE, 0x00, 0x00))
        {
            preambleLength = 4;
            return Encoding.UTF32;
        }

        if (StartsWith(body, 0xFF, 0xFE))
        {
            preambleLength = 2;
            return Encoding.Unicode;
        }

        if (StartsWith(body, 0xFE, 0xFF))
        {
            preambleLength = 2;
            return Encoding.BigEndianUnicode;
        }

        preambleLength = 0;
        return Encoding.UTF8;
    }

    private static int GetPreambleLength(byte[] body, Encoding encoding) => encoding.CodePage switch
    {
        Utf8CodePage => StartsWith(body, 0xEF, 0xBB, 0xBF) ? 3 : 0,
        Utf32CodePage => StartsWith(body, 0xFF, 0xFE, 0x00, 0x00) ? 4 : 0,
        UnicodeCodePage => StartsWith(body, 0xFF, 0xFE) ? 2 : 0,
        BigEndianUnicodeCodePage => StartsWith(body, 0xFE, 0xFF) ? 2 : 0,
        _ => StartsWith(body, encoding.GetPreamble()) ? encoding.GetPreamble().Length : 0,
    };

    private static bool StartsWith(byte[] body, byte first, byte second) =>
        body.Length >= 2 && body[0] == first && body[1] == second;

    private static bool StartsWith(byte[] body, byte first, byte second, byte third) =>
        body.Length >= 3 && body[0] == first && body[1] == second && body[2] == third;

    private static bool StartsWith(byte[] body, byte first, byte second, byte third, byte fourth) =>
        body.Length >= 4 && body[0] == first && body[1] == second && body[2] == third && body[3] == fourth;

    private static bool StartsWith(byte[] body, byte[] prefix)
    {
        if (body.Length < prefix.Length)
        {
            return false;
        }

        for (var index = 0; index < prefix.Length; index++)
        {
            if (body[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }
}
