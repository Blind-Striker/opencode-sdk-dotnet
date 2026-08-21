#if NET
using System.Net.Http.Headers;
#endif
using System.Text;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class ResponseEncodingPolicyTests
{
    private readonly ResponseEncodingPolicy _policy = new();

    [Test]
    public async Task Decode_Should_Slice_A_Utf8_Bom_From_The_Byte_Path()
    {
        var payload = Encoding.UTF8.GetBytes(WireBodyData.HealthOk);
        var body = Encoding.UTF8.GetPreamble().Concat(payload).ToArray();

        var decoded = _policy.Decode(body, charset: null);

        await Assert.That(decoded.DecodedBody).IsNull();
        await Assert.That(decoded.Utf8Body.Span.SequenceEqual(payload)).IsTrue();
    }

    [Test]
    public async Task Decode_Should_Use_A_Declared_Non_Utf8_Encoding_Without_A_Bom()
    {
        var body = Encoding.Unicode.GetBytes(WireBodyData.HealthOk);

        var decoded = _policy.Decode(body, "utf-16");

        await Assert.That(decoded.DecodedBody).IsEqualTo(WireBodyData.HealthOk);
        await Assert.That(decoded.Utf8Body.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Decode_Should_Prefer_A_Utf32_Bom_Over_Its_Utf16_Prefix()
    {
        var body = Encoding.UTF32.GetPreamble().Concat(Encoding.UTF32.GetBytes(WireBodyData.HealthOk)).ToArray();

        var decoded = _policy.Decode(body, charset: null);

        await Assert.That(decoded.DecodedBody).IsEqualTo(WireBodyData.HealthOk);
    }

    [Test]
    public async Task Decode_Should_Return_An_Empty_Body_Before_Validating_The_Charset()
    {
        var decoded = _policy.Decode([], "not-an-encoding");

        await Assert.That(decoded.GetDecodedBody()).IsEmpty();
    }

    [Test]
    public async Task Decode_Should_Accept_A_Quoted_Utf8_Charset()
    {
        var body = Encoding.UTF8.GetBytes(WireBodyData.HealthOk);

        var decoded = _policy.Decode(body, "\"utf-8\"");

        await Assert.That(decoded.DecodedBody).IsNull();
        await Assert.That(decoded.GetDecodedBody()).IsEqualTo(WireBodyData.HealthOk);
    }

    [Test]
    public async Task Decode_Should_Return_Empty_For_A_Bom_Only_Body()
    {
        var decoded = _policy.Decode(Encoding.UTF8.GetPreamble(), charset: null);

        await Assert.That(decoded.GetDecodedBody()).IsEmpty();
    }

#if NET
    [Test]
    public async Task Decode_Should_Match_Modern_HttpContent_String_Decoding()
    {
        var utf8 = Encoding.UTF8.GetBytes(WireBodyData.HealthOk);
        var utf8Bom = Encoding.UTF8.GetPreamble().Concat(utf8).ToArray();
        var utf16 = Encoding.Unicode.GetBytes(WireBodyData.HealthOk);
        var utf32Bom = Encoding.UTF32.GetPreamble().Concat(Encoding.UTF32.GetBytes(WireBodyData.HealthOk)).ToArray();
        (byte[] Body, string? Charset)[] cases =
        [
            (utf8Bom, null),
            (utf8, "utf-8"),
            (utf8Bom, "utf-8"),
            (utf8, "\"utf-8\""),
            (utf16, "utf-16"),
            (utf32Bom, null),
            (Encoding.UTF8.GetPreamble(), null),
            ([], "not-an-encoding"),
        ];

        foreach (var item in cases)
        {
            using var content = new ByteArrayContent(item.Body);
            if (item.Charset is not null)
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = item.Charset };
            }

            var expected = await content.ReadAsStringAsync();
            var actual = _policy.Decode(item.Body, item.Charset).GetDecodedBody();

            await Assert.That(actual).IsEqualTo(expected);
        }
    }
#endif
}
