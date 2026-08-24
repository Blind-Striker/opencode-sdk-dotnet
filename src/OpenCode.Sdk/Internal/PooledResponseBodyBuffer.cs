using System.Buffers;

namespace OpenCode.Sdk.Internal;

/// <summary>Owns the pooled response buffer separately from the Stream facade used by HttpContent.</summary>
internal sealed class PooledResponseBodyBuffer : IDisposable, IAsyncDisposable
{
    private readonly PooledResponseBodyStream _writer;

    public PooledResponseBodyBuffer(ArrayPool<byte> pool, int initialCapacity) =>
        _writer = new PooledResponseBodyStream(pool, initialCapacity);

    public Stream Writer => _writer;

    public byte[] DetachBuffer(out int written) => _writer.DetachBuffer(out written);

    public void Dispose() => _writer.Dispose();

    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}
