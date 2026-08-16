using System.Text;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>Hands out one prepared chunk per read, so a test can place a frame boundary mid-chunk.</summary>
internal sealed class ChunkedStream : Stream
{
    private readonly Queue<byte[]> _chunks;

    private ChunkedStream(IEnumerable<byte[]> chunks)
    {
        _chunks = new Queue<byte[]>(chunks);
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public static ChunkedStream Of(params string[] chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        return new ChunkedStream(chunks.Select(static chunk => Encoding.UTF8.GetBytes(chunk)));
    }

    /// <summary>Splits one payload into fixed-size byte chunks, cutting multi-byte characters apart.</summary>
    public static ChunkedStream OfBytes(string payload, int chunkSize)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        var bytes = Encoding.UTF8.GetBytes(payload);
        var chunks = new List<byte[]>();
        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            chunks.Add([.. bytes.Skip(offset).Take(chunkSize)]);
        }

        return new ChunkedStream(chunks);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (_chunks.Count is 0)
        {
            return 0;
        }

        var chunk = _chunks.Dequeue();
        var length = Math.Min(chunk.Length, count);
        Array.Copy(chunk, 0, buffer, offset, length);
        if (length < chunk.Length)
        {
            _chunks.Enqueue([.. chunk.Skip(length)]);
        }

        return length;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
