namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// Serves a buffered body in reads no larger than one socket-sized chunk, so the reader's
/// decoder carry-over and cross-read frame assembly are measured instead of the 8 KiB whole
/// reads a <see cref="MemoryStream"/> delivers.
/// </summary>
internal sealed class ChunkedReadStream : Stream
{
    private readonly byte[] _body;
    private readonly int _chunkBytes;
    private int _position;

    public ChunkedReadStream(byte[] body, int chunkBytes)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkBytes, 1);
        _body = body;
        _chunkBytes = chunkBytes;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _body.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => ReadChunk(buffer.AsSpan(offset, count));

#if NET
    public override int Read(Span<byte> buffer) => ReadChunk(buffer);
#endif

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<int>(cancellationToken)
            : Task.FromResult(ReadChunk(buffer.AsSpan(offset, count)));

#if NET
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled<int>(cancellationToken)
            : ValueTask.FromResult(ReadChunk(buffer.Span));
#endif

    public override void Flush()
    {
    }

    private int ReadChunk(Span<byte> buffer)
    {
        var count = Math.Min(Math.Min(buffer.Length, _chunkBytes), _body.Length - _position);
        _body.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
