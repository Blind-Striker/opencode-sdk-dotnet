using System.Buffers;
using System.Diagnostics;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Buffers the response body into a pooled array under the progress window: one linked timer
/// spans the send and every read, each read that progresses re-arms it, and interrupting a
/// stalled read disposes the content so even a read no token can reach settles. Only a live
/// event-stream success stays unbuffered, and it leaves this policy with the timer already
/// dead. Knowledge source: BCL-derived — HttpClient's timeout linking and dotnet/runtime's
/// pooled response buffering are the reference designs.
/// </summary>
internal sealed class ResponseBufferingPolicy : PipelinePolicy
{
    /// <summary>The smallest rent, used when the response declares no usable length.</summary>
    private const int InitialRentBytes = 256;

    /// <summary>The largest array the runtime can allocate; growth clamps here and lets the pool refuse beyond it.</summary>
    private const int MaxArrayLength = 0x7FFFFFC7;

    private readonly ArrayPool<byte> _pool;

    public ResponseBufferingPolicy(ArrayPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        _pool = pool;
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, ReadOnlyMemory<PipelinePolicy> remaining)
    {
        // The window is linked over the caller's token and armed before the send, so a
        // server that never answers headers is bounded exactly like a stalled body. The
        // transport sends under NetworkToken; classification keeps reading the caller token.
        using var progress = CancellationTokenSource.CreateLinkedTokenSource(message.CancellationToken);
        message.NetworkToken = progress.Token;
        progress.CancelAfter(message.NetworkTimeout);
        await ProcessNextAsync(message, remaining).ConfigureAwait(false);
        Debug.Assert(message.Response is not null, "The transport writes the response before this policy resumes.");
        if (ShouldBuffer(message))
        {
            message.Body = await ReadAsync(message, progress).ConfigureAwait(false);
        }
    }

    private static bool ShouldBuffer(PipelineMessage message)
    {
        // The one-shot plane buffers every response — an unexpected no-content body is
        // drained here and ignored by the materializer (canon). A 2xx on the stream plane is
        // either the live event stream, which stays open until the caller is done with it, or
        // an undeclared success the plane refuses without reading; everything else is an
        // error body, buffered under the same window as any one-shot body.
        return message.BufferBody || (int)message.Response!.StatusCode is < 200 or >= 300;
    }

    private async Task<ResponseBody> ReadAsync(PipelineMessage message, CancellationTokenSource progress)
    {
        var content = message.Response!.Content;
        if (content is null)
        {
            return ResponseBody.Empty;
        }

        // A read a token cannot reach still has to settle: downlevel socket reads ignore a
        // token once in flight, and HttpContent's own buffering path drops the token before
        // it reaches SerializeToStreamAsync. Interrupting — progress expiry or caller
        // cancellation alike — therefore disposes the content to fail the pending read; the
        // classification below reads the resulting fault against the caller's token, so
        // disposal-induced I/O failures stay caller cancellation when the caller asked and a
        // transport failure when the window did.
        // API-shape divergence, not algorithm: the registration has no DisposeAsync downlevel.
#if NET
        await using var interruption = progress.Token.UnsafeRegister(
            static state => ((HttpContent)state!).Dispose(),
            content).ConfigureAwait(false);
#else
        using var interruption = progress.Token.UnsafeRegister(
            static state => ((HttpContent)state!).Dispose(),
            content);
#endif
        try
        {
            progress.CancelAfter(message.NetworkTimeout);
            var body = await content.ReadAsStreamAsync(progress.Token).ConfigureAwait(false);
            return await CopyToPooledAsync(body, content.Headers.ContentLength, message.NetworkTimeout, progress)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (FailureClassification.Handles(exception, FailurePhase.ResponseBodyRead))
        {
            throw FailureClassification.Map(exception, FailurePhase.ResponseBodyRead, message.CancellationToken);
        }
    }

    private async Task<ResponseBody> CopyToPooledAsync(Stream body, long? declaredLength,
        TimeSpan networkTimeout, CancellationTokenSource progress)
    {
        var bytes = _pool.Rent(InitialRent(declaredLength));
        try
        {
            var filled = 0;
            while (true)
            {
                if (filled == bytes.Length)
                {
                    bytes = Grow(bytes, filled);
                }

                var read = await body.ReadAsync(bytes.AsMemory(filled), progress.Token).ConfigureAwait(false);
                if (read is 0)
                {
                    // Ownership moves to the message; PipelineMessage.Dispose returns the array.
                    return new ResponseBody(_pool, bytes, filled);
                }

                filled += read;
                progress.CancelAfter(networkTimeout);
            }
        }
        catch
        {
            // A failed read has settled before this runs — the copy is never abandoned
            // mid-write — so the array is safe to hand back.
            _pool.Return(bytes);
            throw;
        }
    }

    /// <summary>
    /// Sizes the first rent from the declared length plus the end-of-stream probe byte, so a
    /// correctly declared body never grows; an undeclared length starts small and doubles.
    /// </summary>
    private static int InitialRent(long? declaredLength) =>
        declaredLength is > 0
            ? (int)Math.Clamp(declaredLength.Value + 1, InitialRentBytes, MaxArrayLength)
            : InitialRentBytes;

    private byte[] Grow(byte[] bytes, int filled)
    {
        if (bytes.Length == MaxArrayLength)
        {
            // A body beyond the largest possible array cannot buffer; fail as the read
            // failure it is instead of looping on a rent that can never be bigger.
            throw new IOException("The opencode response body exceeds the largest bufferable size.");
        }

        var grown = _pool.Rent((int)Math.Min(bytes.Length * 2L, MaxArrayLength));
        bytes.AsSpan(0, filled).CopyTo(grown);
        _pool.Return(bytes);
        return grown;
    }
}
