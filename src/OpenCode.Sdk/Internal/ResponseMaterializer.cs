namespace OpenCode.Sdk.Internal;

/// <summary>
/// Materializes a completed pipeline message for both planes through one decoding policy:
/// success bodies materialize from validated UTF-8 when possible, and error bodies stay
/// decoded strings so both error channels retain the exact raw body.
/// </summary>
internal sealed class ResponseMaterializer
{
    private readonly ResponseEncodingPolicy _encodingPolicy = new();

    /// <summary>Maps the buffered response onto the operation's typed envelope.</summary>
    public TResponse Materialize<TResponse>(PipelineMessage message, ResponseAdapter<TResponse> adapter)
        where TResponse : OpenCodeResponse
    {
        var status = (int)message.Response!.StatusCode;
        switch (adapter.Classify(status))
        {
            case StatusVerdict.Success:
                var encodedBody = Decode(message);
                return encodedBody.DecodedBody is { } decoded
                    ? adapter.Adapt(status, decoded)
                    : adapter.AdaptSuccess(status, encodedBody.Utf8Body.Span);
            case StatusVerdict.NoContentSuccess:
                // An unexpected body was drained into the buffer and is ignored here (canon).
                return adapter.AdaptSuccess(status, []);
            case StatusVerdict.DeclaredError:
            case StatusVerdict.UndeclaredError:
                return adapter.Adapt(status, Decode(message).GetDecodedBody());
            case StatusVerdict.UndeclaredSuccess:
                throw StatusVerdictFailures.UndeclaredSuccess(status);
            default:
                throw new InvalidOperationException("The adapter produced an unknown status verdict.");
        }
    }

    /// <summary>Reads a buffered error body as the decoded string both error channels retain.</summary>
    public string ReadErrorBody(PipelineMessage message) => Decode(message).GetDecodedBody();

    private EncodedResponseBody Decode(PipelineMessage message)
    {
        try
        {
            return _encodingPolicy.Decode(
                message.Body?.Bytes ?? [],
                message.Response!.Content?.Headers.ContentType?.CharSet);
        }
        catch (Exception exception) when (FailureClassification.Handles(exception, FailurePhase.ResponseBodyRead))
        {
            // The declared-charset refusal surfaces here as an InvalidOperationException and
            // stays a body-read failure, exactly as it was when decoding rode the read itself.
            throw FailureClassification.Map(exception, FailurePhase.ResponseBodyRead, message.CancellationToken);
        }
    }
}
