using System.Buffers;
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
        return DecodeCore(body, body.Length, charset, pool: null);
    }

    internal EncodedResponseBody DecodeOwned(ArrayPool<byte> pool, byte[] body, int length, string? charset)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            return DecodeCore(body, length, charset, pool);
        }
        catch
        {
            pool.Return(body, clearArray: false);
            throw;
        }
    }

    private EncodedResponseBody DecodeCore(byte[] body, int length, string? charset, ArrayPool<byte>? pool)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, body.Length);
        if (length is 0)
        {
            // HttpContent returns an empty string before consulting an invalid charset.
            return Create(body.AsMemory(0, 0), decodedBody: null, pool, body);
        }

        var encoding = ResolveEncoding(body, length, charset, out var preambleLength);
        if (encoding.CodePage == Utf8CodePage && IsValidUtf8(body, preambleLength, length))
        {
            return Create(body.AsMemory(preambleLength, length - preambleLength), decodedBody: null, pool, body);
        }

        return Create(
            utf8Body: default,
            encoding.GetString(body, preambleLength, length - preambleLength),
            pool,
            body);
    }

    private static EncodedResponseBody Create(ReadOnlyMemory<byte> utf8Body, string? decodedBody,
        ArrayPool<byte>? pool, byte[] body) =>
        pool is null
            ? EncodedResponseBody.Borrowed(utf8Body, decodedBody)
            : EncodedResponseBody.Owned(utf8Body, decodedBody, pool, body);

    private bool IsValidUtf8(byte[] body, int offset, int length)
    {
        try
        {
            _ = _strictUtf8.GetCharCount(body, offset, length - offset);
            return true;
        }
        catch (DecoderFallbackException)
        {
            // ReadAsStringAsync replacement-decodes malformed UTF-8. Preserve that path.
            return false;
        }
    }

    private static Encoding ResolveEncoding(byte[] body, int length, string? charset, out int preambleLength)
    {
        if (charset is not null)
        {
            try
            {
                var unquoted = charset.Length > 2 && charset[0] is '"' && charset[^1] is '"'
                    ? charset[1..^1]
                    : charset;
                var declared = Encoding.GetEncoding(unquoted);
                preambleLength = GetPreambleLength(body, length, declared);
                return declared;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                throw new InvalidOperationException("The response content declared an invalid charset.", exception);
            }
        }

        if (StartsWith(body, length, 0xEF, 0xBB, 0xBF))
        {
            preambleLength = 3;
            return Encoding.UTF8;
        }

        if (StartsWith(body, length, 0xFF, 0xFE, 0x00, 0x00))
        {
            preambleLength = 4;
            return Encoding.UTF32;
        }

        if (StartsWith(body, length, 0xFF, 0xFE))
        {
            preambleLength = 2;
            return Encoding.Unicode;
        }

        if (StartsWith(body, length, 0xFE, 0xFF))
        {
            preambleLength = 2;
            return Encoding.BigEndianUnicode;
        }

        preambleLength = 0;
        return Encoding.UTF8;
    }

    private static int GetPreambleLength(byte[] body, int length, Encoding encoding) => encoding.CodePage switch
    {
        Utf8CodePage => StartsWith(body, length, 0xEF, 0xBB, 0xBF) ? 3 : 0,
        Utf32CodePage => StartsWith(body, length, 0xFF, 0xFE, 0x00, 0x00) ? 4 : 0,
        UnicodeCodePage => StartsWith(body, length, 0xFF, 0xFE) ? 2 : 0,
        BigEndianUnicodeCodePage => StartsWith(body, length, 0xFE, 0xFF) ? 2 : 0,
        _ => GetOtherPreambleLength(body, length, encoding),
    };

    private static int GetOtherPreambleLength(byte[] body, int length, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        return StartsWith(body, length, preamble) ? preamble.Length : 0;
    }

    private static bool StartsWith(byte[] body, int length, byte first, byte second) =>
        length >= 2 && body[0] == first && body[1] == second;

    private static bool StartsWith(byte[] body, int length, byte first, byte second, byte third) =>
        length >= 3 && body[0] == first && body[1] == second && body[2] == third;

    private static bool StartsWith(byte[] body, int length, byte first, byte second, byte third, byte fourth) =>
        length >= 4 && body[0] == first && body[1] == second && body[2] == third && body[3] == fourth;

    private static bool StartsWith(byte[] body, int length, byte[] prefix)
    {
        if (length < prefix.Length)
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
