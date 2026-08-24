using System.Net;
using System.Text;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class ResponseBodyReaderTests
{
    [Test]
    public async Task ReadAsync_Should_Keep_The_Pooled_Buffer_Until_The_Lease_Is_Disposed()
    {
        var pool = new TrackingByteArrayPool();
        var payload = Encoding.UTF8.GetBytes(new string('x', 160));
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        };
        var reader = new ResponseBodyReader(pool);

        var body = await reader.ReadAsync(response, Timeout.InfiniteTimeSpan, CancellationToken.None);

        await Assert.That(body.Utf8Body.Span.SequenceEqual(payload)).IsTrue();
        await Assert.That(pool.ReturnCount).IsEqualTo(0);
        body.Dispose();
        body.Dispose();
        await Assert.That(pool.ReturnCount).IsEqualTo(1);
        await Assert.That(pool.OutstandingCount).IsEqualTo(0);
        _ = Assert.Throws<ObjectDisposedException>(() => _ = body.Utf8Body);
    }

    [Test]
    public async Task ReadAsync_Should_Grow_Past_An_Incorrect_Content_Length()
    {
        var pool = new TrackingByteArrayPool();
        var payload = Encoding.UTF8.GetBytes(new string('x', 160));
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new MisreportedLengthContent(payload),
        };
        var reader = new ResponseBodyReader(pool);

        using var body = await reader.ReadAsync(response, Timeout.InfiniteTimeSpan, CancellationToken.None);

        await Assert.That(body.Utf8Body.Span.SequenceEqual(payload)).IsTrue();
        await Assert.That(pool.RentCount).IsGreaterThan(1);
    }

    [Test]
    public async Task ReadAsync_Should_Return_The_Destination_Buffer_After_Timeout()
    {
        var pool = new TrackingByteArrayPool();
        using var content = new BlockingContent();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content, };
        var reader = new ResponseBodyReader(pool);

        var exception = await Assert
            .That(async () => _ = await reader.ReadAsync(
                response, TimeSpan.FromMilliseconds(50), CancellationToken.None))
            .Throws<OpenCodeTransportException>();

        await Assert.That(exception!.InnerException).IsTypeOf<TimeoutException>();
        await content.ReadCompleted.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.That(pool.OutstandingCount).IsEqualTo(0);
        await Assert.That(pool.ReturnCount).IsEqualTo(pool.RentCount);
    }

    private sealed class MisreportedLengthContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body, 0, body.Length);

#if NET
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken) =>
            stream.WriteAsync(body, cancellationToken).AsTask();
#endif

        protected override bool TryComputeLength(out long length)
        {
            length = 1;
            return true;
        }
    }
}
