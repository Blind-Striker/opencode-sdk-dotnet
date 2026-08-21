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
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            throw new OpenCodeTransportException("The opencode response body could not be read.", exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            response.Content?.Dispose();
            ObserveFault(pendingRead);
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new OpenCodeTransportException("The opencode response body could not be read.", exception);
        }
        catch (TimeoutException exception)
        {
            response.Content?.Dispose();
            ObserveFault(pendingRead);
            throw new OpenCodeTransportException("The opencode response body could not be read.", exception);
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
