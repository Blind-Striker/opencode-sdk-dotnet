using System.Buffers;

namespace OpenCode.Sdk.Internal;

/// <summary>Buffers one response body into an ArrayPool-backed writable stream.</summary>
internal sealed class PooledResponseBodyStream : Stream
{
    private readonly Lock _gate = new();
    private readonly ArrayPool<byte> _pool;
    private byte[]? _buffer;
    private int _written;

    public PooledResponseBodyStream(ArrayPool<byte> pool, int initialCapacity)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfLessThan(initialCapacity, 1);

        _pool = pool;
        _buffer = pool.Rent(initialCapacity);
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_buffer is null, this);
                return _written;
            }
        }
    }

    public override long Position
    {
        get => Length;
        set => throw new NotSupportedException();
    }

    public byte[] DetachBuffer(out int written)
    {
        lock (_gate)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledResponseBodyStream));
            _buffer = null;
            written = _written;
            return buffer;
        }
    }

    public override void Flush()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
        }

        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The offset and count exceed the source buffer.", nameof(count));
        }

        WriteCore(buffer, offset, count);
    }

#if NET
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        lock (_gate)
        {
            var destination = EnsureCapacity(buffer.Length);
            buffer.CopyTo(destination.AsSpan(_written));
            _written += buffer.Length;
        }
    }
#endif

    public override void WriteByte(byte value)
    {
        lock (_gate)
        {
            var destination = EnsureCapacity(1);
            destination[_written++] = value;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The offset and count exceed the source buffer.", nameof(count));
        }

        WriteCore(buffer, offset, count);
        return Task.CompletedTask;
    }

#if NET
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var destination = EnsureCapacity(buffer.Length);
            buffer.Span.CopyTo(destination.AsSpan(_written));
            _written += buffer.Length;
        }

        return ValueTask.CompletedTask;
    }
#endif

    private void WriteCore(byte[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            var destination = EnsureCapacity(count);
            Buffer.BlockCopy(buffer, offset, destination, _written, count);
            _written += count;
        }
    }

    protected override void Dispose(bool disposing)
    {
        byte[]? returned = null;
        if (disposing)
        {
            lock (_gate)
            {
                returned = _buffer;
                _buffer = null;
            }
        }

        if (returned is not null)
        {
            _pool.Return(returned, clearArray: false);
        }

        base.Dispose(disposing);
    }

    private byte[] EnsureCapacity(int appended)
    {
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledResponseBodyStream));
        var required = (long)_written + appended;
        if (required > int.MaxValue)
        {
            throw new IOException("The opencode response body is too large to buffer.");
        }

        if (required <= buffer.Length)
        {
            return buffer;
        }

        var doubled = Math.Min((long)buffer.Length * 2, int.MaxValue);
        var replacement = _pool.Rent((int)Math.Max(required, doubled));
        Buffer.BlockCopy(buffer, 0, replacement, 0, _written);
        _buffer = replacement;
        _pool.Return(buffer, clearArray: false);
        return replacement;
    }
}
