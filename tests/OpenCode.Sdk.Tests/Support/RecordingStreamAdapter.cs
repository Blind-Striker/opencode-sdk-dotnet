using System.Text.Json.Serialization.Metadata;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>A stream contract over the test payload, reading errors against declared tags.</summary>
internal sealed class RecordingStreamAdapter : IStreamAdapter<TestBody>
{
    private readonly IReadOnlyCollection<string>? _allowedTags;

    public RecordingStreamAdapter(params string[] allowedTags)
    {
        _allowedTags = allowedTags.Length is 0 ? null : allowedTags;
    }

    public JsonTypeInfo<TestBody> PayloadTypeInfo => TestBodyJsonContext.Default.TestBody;

    public OpenCodeError? ReadError(int status, string rawBody) => OpenCodeErrorReader.Read(rawBody, _allowedTags);
}
