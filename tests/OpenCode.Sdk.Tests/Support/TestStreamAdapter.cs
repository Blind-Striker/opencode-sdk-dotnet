using System.Text.Json.Serialization.Metadata;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>A stream contract over the test payload, reading errors against declared tags.</summary>
internal sealed class TestStreamAdapter : IStreamAdapter<TestBody>
{
    /// <summary>The name the pinned contract gives a mid-stream failure frame.</summary>
    public const string StreamFailureEventName = "effect/httpapi/stream/failure";

    private readonly IReadOnlyCollection<string>? _allowedTags;

    public TestStreamAdapter(params string[] allowedTags)
    {
        _allowedTags = allowedTags.Length is 0 ? null : allowedTags;
    }

    public string FailureEventName => StreamFailureEventName;

    public JsonTypeInfo<TestBody> PayloadTypeInfo => TestBodyJsonContext.Default.TestBody;

    public IOpenCodeError? ReadError(int status, string rawBody) => OpenCodeErrorReader.Read(rawBody, _allowedTags);
}
