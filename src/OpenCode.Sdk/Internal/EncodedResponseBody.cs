namespace OpenCode.Sdk.Internal;

/// <summary>Holds either validated UTF-8 bytes or a body decoded through its declared/BOM encoding.</summary>
internal readonly record struct EncodedResponseBody(ReadOnlyMemory<byte> Utf8Body, string? DecodedBody);
