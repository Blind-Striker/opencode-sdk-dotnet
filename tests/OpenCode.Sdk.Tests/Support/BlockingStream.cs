namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Serves one prepared prefix and then blocks until the read is canceled, standing in for a
/// live event stream that is idle between events. The block never elapses on its own, so a
/// test that reaches it is answered by cancellation alone.
/// </summary>
internal sealed class BlockingStream : Stream
{
    private readonly byte[] _prefix;
    private int _offset;

    public BlockingStream(byte[] prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        _prefix = prefix;
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

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (_offset < _prefix.Length)
        {
            var length = Math.Min(_prefix.Length - _offset, count);
            Array.Copy(_prefix, _offset, buffer, offset, length);
            _offset += length;
            return length;
        }

        // Nothing ever completes this, so only the token can answer the read.
        var block = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => block.TrySetCanceled(cancellationToken));
        return await block.Task;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
