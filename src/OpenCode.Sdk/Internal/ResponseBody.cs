namespace OpenCode.Sdk.Internal;

/// <summary>
/// One buffered response body, still encoded as the wire sent it. Written by
/// <see cref="ResponseBufferingPolicy"/>, consumed by <see cref="ResponseMaterializer"/>.
/// </summary>
internal sealed class ResponseBody
{
    private readonly byte[] _bytes;

    public ResponseBody(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        _bytes = bytes;
    }

    /// <summary>Gets the raw buffered bytes.</summary>
    public byte[] Bytes => _bytes;
}
