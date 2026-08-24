using System.Buffers;

namespace OpenCode.Sdk.Internal;

/// <summary>Reads and decodes a response body within the remaining transport timeout.</summary>
internal sealed class ResponseBodyReader
{
    private const int DefaultInitialCapacity = 8192;

    private readonly ArrayPool<byte> _pool;
    private readonly ResponseEncodingPolicy _encodingPolicy = new();

    public ResponseBodyReader()
        : this(ArrayPool<byte>.Shared)
    {
    }

    internal ResponseBodyReader(ArrayPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        _pool = pool;
    }

    public async Task<EncodedResponseBody> ReadAsync(HttpResponseMessage response, TimeSpan remainingTimeout, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return _encodingPolicy.Decode([], charset: null);
        }

        int initialCapacity;
        try
        {
            initialCapacity = InitialCapacity(response.Content.Headers.ContentLength);
        }
        catch (IOException exception)
        {
            throw new OpenCodeTransportException("The opencode response body could not be read.", exception);
        }

        var destination = new PooledResponseBodyBuffer(_pool, initialCapacity);
        await using var destinationLifetime = destination.ConfigureAwait(false);
        Task? pendingRead = null;
        try
        {
            pendingRead = CopyToAsync(response.Content, destination.Writer, cancellationToken);
            if (remainingTimeout != Timeout.InfiniteTimeSpan)
            {
                await pendingRead
                    .WaitAsync(remainingTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (cancellationToken.CanBeCanceled)
            {
                await pendingRead.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await pendingRead.ConfigureAwait(false);
            }

            var buffer = destination.DetachBuffer(out var written);
            return _encodingPolicy.DecodeOwned(_pool, buffer, written, response.Content.Headers.ContentType?.CharSet);
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

    private static int InitialCapacity(long? contentLength)
    {
        if (contentLength is null)
        {
            return DefaultInitialCapacity;
        }

        if (contentLength > int.MaxValue)
        {
            throw new IOException("The opencode response body is too large to buffer.");
        }

        return Math.Max((int)contentLength.Value, 1);
    }

    private static Task CopyToAsync(HttpContent content, Stream destination, CancellationToken cancellationToken)
    {
#if NET
        return content.CopyToAsync(destination, cancellationToken);
#else
        // Retain the real task: downlevel token wrappers abandon the underlying copy on cancellation.
        _ = cancellationToken;
        return content.CopyToAsync(destination);
#endif
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
