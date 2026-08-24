using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// The success-body decoder in isolation: valid UTF-8 at three sizes (the validation pass that
/// lets materialization read bytes directly) against the fallbacks that decode to a string — a
/// UTF-8 BOM, a declared charset, a UTF-16 body, and malformed UTF-8 taking replacement decoding.
/// </summary>
[MemoryDiagnoser]
public class ResponseEncodingPolicyBenchmarks
{
    private const int MediumParts = 120;

    public static IEnumerable<WireFixture> Fixtures()
    {
        var deep = BenchmarkFixtures.DeepAssistantMessage();
        var composer = new AssistantMessageComposer(deep);
        yield return Body("utf8-health", BenchmarkFixtures.HealthBody());
        yield return Body("utf8-deep", deep);
        yield return Body("utf8-medium", composer.WithContentParts(MediumParts));
        yield return Body("utf8-bom-deep", BodyEncodings.WithUtf8Bom(deep));
        yield return Body("declared-utf8-deep", deep, "utf-8");
        yield return Body("utf16-bom-deep", BodyEncodings.AsUtf16(deep));
        yield return Body("malformed-utf8-deep", BodyEncodings.WithMalformedUtf8(deep));
    }

    [GlobalSetup]
    public void Setup()
    {
        foreach (var fixture in Fixtures())
        {
            var decoded = ResponseEncodingPolicy.Decode(fixture.Bytes, fixture.Charset);
            var expectsString = fixture.Name.StartsWith("utf16", StringComparison.Ordinal) || fixture.Name.StartsWith("malformed", StringComparison.Ordinal);
            if (expectsString ? decoded.DecodedBody is null : decoded.DecodedBody is not null || decoded.Utf8Body.IsEmpty)
            {
                throw new InvalidOperationException($"Fixture '{fixture.Name}' did not take the expected decoding path.");
            }
        }
    }

    /// <summary>
    /// Validates or decodes one body exactly as the pipeline does before adaptation, returning
    /// the materialized length so the result is consumed: validated UTF-8 bytes on the direct
    /// path, decoded UTF-16 characters on the fallback.
    /// </summary>
    [Benchmark]
    [ArgumentsSource(nameof(Fixtures))]
    public int Decode(WireFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var decoded = ResponseEncodingPolicy.Decode(fixture.Bytes, fixture.Charset);
        return decoded.DecodedBody?.Length ?? decoded.Utf8Body.Length;
    }

    private static WireFixture Body(string name, byte[] body, string? charset = null) =>
        new(name, body, items: 1, payloadBytesPerItem: body.Length, charset);
}
