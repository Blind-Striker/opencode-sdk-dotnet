using System.Text.Json.Serialization.Metadata;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Carries one streaming operation's contract across the generated-to-hand-written seam:
/// how each frame's payload deserializes, and how a status refused before the stream opens
/// maps onto a typed error. A stream has no envelope to answer on, so an error always throws.
/// </summary>
/// <typeparam name="TPayload">The type each event frame carries.</typeparam>
internal interface IStreamAdapter<TPayload>
{
    /// <summary>
    /// Gets the event name a mid-stream failure frame carries, from the operation's declared
    /// <c>x-effect-stream.failureEvent</c>. A frame answering to this name reports the
    /// stream's failure instead of carrying a payload.
    /// </summary>
    public string FailureEventName { get; }

    /// <summary>Gets the source-generated metadata each frame's payload is read through.</summary>
    public JsonTypeInfo<TPayload> PayloadTypeInfo { get; }

    /// <summary>Maps an error status onto its declared tags, tolerating an unparseable body.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="rawBody">The buffered error body.</param>
    /// <returns>The typed error, or <see langword="null"/> when the body could not be parsed.</returns>
    public IOpenCodeError? ReadError(int status, string rawBody);
}
