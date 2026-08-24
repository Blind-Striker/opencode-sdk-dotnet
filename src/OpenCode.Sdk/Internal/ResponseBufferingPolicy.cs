using System.Diagnostics;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Buffers the response body inside the transport budget so no plane reads from a live
/// socket: the budget spans send and read, an abandoned read is interrupted by disposing the
/// content, and only a live event-stream success stays unbuffered for the caller to frame.
/// Knowledge source: BCL-derived — <see cref="HttpContent"/> read and task wait machinery.
/// </summary>
internal sealed class ResponseBufferingPolicy : PipelinePolicy
{
    public override async ValueTask ProcessAsync(PipelineMessage message, ReadOnlyMemory<PipelinePolicy> remaining)
    {
        var requestStarted = Stopwatch.GetTimestamp();
        await ProcessNextAsync(message, remaining).ConfigureAwait(false);
        Debug.Assert(message.Response is not null, "The transport writes the response before this policy resumes.");
        if (!ShouldBuffer(message))
        {
            return;
        }

        message.Body = await ReadAsync(message, GetRemainingTimeout(message.NetworkTimeout, requestStarted))
            .ConfigureAwait(false);
    }

    private static bool ShouldBuffer(PipelineMessage message)
    {
        var status = (int)message.Response!.StatusCode;
        if (message.BufferBody)
        {
            // A declared no-content success never reads an unexpected body; it is disposed
            // with the response. Every other status buffers before the materializer runs.
            return status != message.NoBodySuccessStatus;
        }

        // A 2xx on the stream plane is either the live event stream, which stays open until
        // the caller is done with it, or an undeclared success the plane refuses without
        // reading. Everything else is an error body, buffered under the same budget as any
        // one-shot body.
        return status is < 200 or >= 300;
    }

    private static TimeSpan GetRemainingTimeout(TimeSpan totalTimeout, long requestStarted)
    {
        if (totalTimeout == Timeout.InfiniteTimeSpan)
        {
            return Timeout.InfiniteTimeSpan;
        }

        var elapsedSeconds = (Stopwatch.GetTimestamp() - requestStarted) / (double)Stopwatch.Frequency;
        var remaining = totalTimeout - TimeSpan.FromSeconds(elapsedSeconds);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static async Task<ResponseBody> ReadAsync(PipelineMessage message, TimeSpan remainingTimeout)
    {
        var response = message.Response!;
        var cancellationToken = message.CancellationToken;
        Task<byte[]>? pendingRead = null;
        try
        {
            byte[] bytes;
            if (response.Content is null)
            {
                bytes = [];
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
                    bytes = await pendingRead
                        .WaitAsync(remainingTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (cancellationToken.CanBeCanceled)
                {
                    bytes = await pendingRead.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    bytes = await pendingRead.ConfigureAwait(false);
                }
            }

            return new ResponseBody(bytes);
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
