using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenCode.Sdk.Internal;

/// <summary>Holds either validated UTF-8 bytes or a body decoded through its declared/BOM encoding.</summary>
internal sealed class EncodedResponseBody : IDisposable
{
    private readonly ArrayPool<byte>? _pool;
    private byte[]? _pooledBuffer;
    private string? _decodedBody;
    private ReadOnlyMemory<byte> _utf8Body;
    private bool _disposed;

    private EncodedResponseBody(ReadOnlyMemory<byte> utf8Body, string? decodedBody,
        ArrayPool<byte>? pool, byte[]? pooledBuffer)
    {
        _utf8Body = utf8Body;
        _decodedBody = decodedBody;
        _pool = pool;
        _pooledBuffer = pooledBuffer;
    }

    public string? DecodedBody
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _decodedBody;
        }
    }

    public ReadOnlyMemory<byte> Utf8Body
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _utf8Body;
        }
    }

    internal static EncodedResponseBody Borrowed(ReadOnlyMemory<byte> utf8Body, string? decodedBody) =>
        new(utf8Body, decodedBody, pool: null, pooledBuffer: null);

    internal static EncodedResponseBody Owned(ReadOnlyMemory<byte> utf8Body, string? decodedBody,
        ArrayPool<byte> pool, byte[] pooledBuffer) =>
        new(utf8Body, decodedBody, pool, pooledBuffer);

    public string GetDecodedBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_decodedBody is { } decoded)
        {
            return decoded;
        }

        if (_utf8Body.IsEmpty)
        {
            return string.Empty;
        }

        return MemoryMarshal.TryGetArray(_utf8Body, out var segment) && segment.Array is not null
            ? Encoding.UTF8.GetString(segment.Array, segment.Offset, segment.Count)
            : Encoding.UTF8.GetString(_utf8Body.ToArray());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _utf8Body = default;
        _decodedBody = null;
        if (_pooledBuffer is { } buffer)
        {
            _pooledBuffer = null;
            _pool!.Return(buffer, clearArray: false);
        }
    }
}
