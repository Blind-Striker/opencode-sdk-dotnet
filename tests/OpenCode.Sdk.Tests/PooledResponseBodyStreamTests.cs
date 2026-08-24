using System.Text;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class PooledResponseBodyStreamTests
{
    [Test]
    public async Task Write_Should_Grow_And_Preserve_Only_Written_Bytes()
    {
        var pool = new TrackingByteArrayPool();
        var payload = Encoding.UTF8.GetBytes(new string('x', 160));
        using var stream = new PooledResponseBodyStream(pool, initialCapacity: 1);
        await WriteAsync(stream, payload, 0, 40, CancellationToken.None);
        await WriteAsync(stream, payload, 40, payload.Length - 40, CancellationToken.None);

        var buffer = stream.DetachBuffer(out var written);
        try
        {
            await Assert.That(written).IsEqualTo(payload.Length);
            await Assert.That(buffer.AsSpan(0, written).SequenceEqual(payload)).IsTrue();
            await Assert.That(pool.RentCount).IsGreaterThan(1);
            await Assert.That(pool.OutstandingCount).IsEqualTo(1);
        }
        finally
        {
            pool.Return(buffer);
        }

        await Assert.That(pool.OutstandingCount).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_Should_Return_The_Buffer_Once_And_Reject_Late_Writes()
    {
        var pool = new TrackingByteArrayPool();
        await using var buffer = new PooledResponseBodyBuffer(pool, initialCapacity: 8);
        await WriteAsync(buffer.Writer, [1], 0, 1, CancellationToken.None);

        await buffer.DisposeAsync();
        await buffer.DisposeAsync();

        await Assert.That(pool.ReturnCount).IsEqualTo(1);
        await Assert.That(pool.OutstandingCount).IsEqualTo(0);
        _ = await Assert
            .That(async () => await WriteAsync(buffer.Writer, [2], 0, 1, CancellationToken.None))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task WriteAsync_Should_Not_Mutate_The_Buffer_When_PreCanceled()
    {
        var pool = new TrackingByteArrayPool();
        using var stream = new PooledResponseBodyStream(pool, initialCapacity: 8);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        _ = await Assert
            .That(async () => await WriteAsync(stream, [1, 2, 3], 0, 3, cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(stream.Length).IsEqualTo(0);
    }

    private static Task WriteAsync(Stream stream, byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
#if NET
        return stream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
#else
        return stream.WriteAsync(buffer, offset, count, cancellationToken);
#endif
    }

}
