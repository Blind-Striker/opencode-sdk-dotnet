using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class ServerSentEventReaderTests
{
    [Test]
    public async Task ReadAsync_Should_Yield_One_Payload_Per_Frame()
    {
        using var stream = ChunkedStream.Of("data: {\"a\":1}\n\ndata: {\"a\":2}\n\n");

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.SequenceEqual(["{\"a\":1}", "{\"a\":2}"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ReadAsync_Should_Join_Multi_Line_Data_With_Newlines()
    {
        using var stream = ChunkedStream.Of("data: first\ndata: second\n\n");

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.Single()).IsEqualTo("first\nsecond");
    }

    [Test]
    public async Task ReadAsync_Should_Ignore_Every_Field_Except_Data()
    {
        using var stream = ChunkedStream.Of(": heartbeat\nid: 42\nevent: message\nretry: 500\ndata: kept\n\n");

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.Single()).IsEqualTo("kept");
    }

    [Test]
    public async Task ReadAsync_Should_Skip_A_Frame_Carrying_No_Data()
    {
        using var stream = ChunkedStream.Of(": heartbeat\n\ndata: kept\n\n");

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.Single()).IsEqualTo("kept");
    }

    [Test]
    public async Task ReadAsync_Should_Reassemble_A_Frame_Split_Across_Chunks()
    {
        using var stream = ChunkedStream.Of("data: {\"a", "\":1}\n", "\ndata: {\"a\":2}\n\n");

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.SequenceEqual(["{\"a\":1}", "{\"a\":2}"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ReadAsync_Should_Reassemble_A_Multi_Byte_Character_Split_Across_Chunks()
    {
        // The 'ş' encodes as two bytes; the split lands between them.
        using var stream = ChunkedStream.OfBytes("data: işler\n\n", chunkSize: 8);

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.Single()).IsEqualTo("işler");
    }

    [Test]
    public async Task ReadAsync_Should_Normalize_Crlf_Line_Endings()
    {
        using var stream = ChunkedStream.Of("data: first\r\n\r\ndata: second\r\n\r\n");

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.SequenceEqual(["first", "second"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ReadAsync_Should_Normalize_A_Carriage_Return_Split_Across_Chunks()
    {
        using var stream = ChunkedStream.Of("data: first\r", "\n\r\ndata: second\r\n\r\n");

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.SequenceEqual(["first", "second"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ReadAsync_Should_Flush_A_Trailing_Frame_Without_Its_Blank_Line()
    {
        using var stream = ChunkedStream.Of("data: last\n");

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.Single()).IsEqualTo("last");
    }

    [Test]
    public async Task ReadAsync_Should_Strip_Exactly_One_Space_After_The_Field_Name()
    {
        using var stream = ChunkedStream.Of("data:  padded\n\ndata:tight\n\n");

        var frames = await ReadAllAsync(stream);

        await Assert.That(frames.SequenceEqual([" padded", "tight"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ReadAsync_Should_Refuse_A_Frame_Beyond_The_Size_Limit()
    {
        using var stream = ChunkedStream.Of($"data: {new string('x', 64)}\n\n");

        _ = await Assert
            .That(async () => _ = await ReadAllAsync(stream, maxFrameCharacters: 16))
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ReadAsync_Should_Honor_Cancellation()
    {
        using var stream = ChunkedStream.Of("data: first\n\n");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        _ = await Assert
            .That(async () => _ = await ReadAllAsync(stream, cancellationToken: cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    private static async Task<List<string>> ReadAllAsync(Stream stream,
        int maxFrameCharacters = ServerSentEventReader.DefaultMaxFrameCharacters,
        CancellationToken cancellationToken = default)
    {
        var frames = new List<string>();
        await foreach (var frame in new ServerSentEventReader(maxFrameCharacters).ReadAsync(stream, cancellationToken))
        {
            frames.Add(frame);
        }

        return frames;
    }
}
