namespace OpenCode.Sdk.Internal;

/// <summary>Reads and decodes a response body within the remaining transport timeout.</summary>
internal sealed class ResponseBodyReader
{
    private readonly ResponseEncodingPolicy _encodingPolicy = new();

    public async Task<EncodedResponseBody> ReadAsync(HttpResponseMessage response,
        TimeSpan remainingTimeout, CancellationToken cancellationToken)
    {
        Task<byte[]>? pendingRead = null;
        try
        {
            byte[] body;
            if (response.Content is null)
            {
                body = [];
            }
            else
            {
#if NET
                pendingRead = response.Content.ReadAsByteArrayAsync(cancellationToken);
#else
                // Retain the real task: Polyfill's token overload wraps and abandons it on cancellation.
                pendingRead = response.Content.ReadAsByteArrayAsync();
#endif
                if (remainingTimeout != Timeout.InfiniteTimeSpan)
                {
                    body = await pendingRead
                        .WaitAsync(remainingTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (cancellationToken.CanBeCanceled)
                {
                    body = await pendingRead.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    body = await pendingRead.ConfigureAwait(false);
                }
            }

            return _encodingPolicy.Decode(body, response.Content?.Headers.ContentType?.CharSet);
        }
        catch (Exception exception) when (FailureClassification.Handles(exception, FailurePhase.ResponseBodyRead))
        {
            // A wait that gave up leaves the pending read running: the content is disposed to
            // interrupt it and the abandoned task's fault is observed. A settled fault needs
            // neither, and both are harmless when the race cannot be told apart.
            if (exception is TimeoutException || cancellationToken.IsCancellationRequested)
            {
                response.Content?.Dispose();
                ObserveFault(pendingRead);
            }

            throw FailureClassification.Map(exception, FailurePhase.ResponseBodyRead, cancellationToken);
        }
    }

    private static void ObserveFault(Task? task)
    {
        if (task is null)
        {
            return;
        }

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
