namespace OpenCode.Sdk.Internal;

/// <summary>
/// One operation's trip through the policy pipeline. The planes construct it, the policies
/// write their results onto it, and disposing it releases the request and any response —
/// the message owns both lifetimes so no policy or plane carries a dispose obligation.
/// </summary>
internal sealed class PipelineMessage : IDisposable
{
    /// <summary>Gets the decorated request; built by the plane, sent by <see cref="TransportPolicy"/>.</summary>
    public required HttpRequestMessage Request { get; init; }

    /// <summary>Gets the caller's token, inspected first by every failure classification.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Gets the transport budget the body read must fit inside; written by the plane from the
    /// client's timeout, read by <see cref="ResponseBufferingPolicy"/>.
    /// </summary>
    public TimeSpan NetworkTimeout { get; init; }

    /// <summary>
    /// Gets whether a success body buffers. The stream plane sets <see langword="false"/> so a
    /// live event stream stays open; error statuses buffer regardless.
    /// </summary>
    public bool BufferBody { get; init; } = true;

    /// <summary>
    /// Gets the declared success status whose body is not read, or <see langword="null"/> when
    /// the declared success carries one. Written by the one-shot plane from the adapter's
    /// contract, read by <see cref="ResponseBufferingPolicy"/>.
    /// </summary>
    public int? NoBodySuccessStatus { get; init; }

    /// <summary>Gets the response; written by <see cref="TransportPolicy"/> after a classified send.</summary>
    public HttpResponseMessage? Response { get; internal set; }

    /// <summary>
    /// Gets the buffered body; written by <see cref="ResponseBufferingPolicy"/>, consumed by
    /// <see cref="ResponseMaterializer"/>.
    /// </summary>
    public ResponseBody? Body { get; internal set; }

    public void Dispose()
    {
        Response?.Dispose();
        Request.Dispose();
    }
}
