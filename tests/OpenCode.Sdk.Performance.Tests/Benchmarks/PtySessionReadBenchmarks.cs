using System.Net.WebSockets;
using System.Text;
using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// The PTY WebSocket read path in isolation — frame decode, replay/cursor handling, and the
/// delivered frame — with no live server and no real socket: a canned <see cref="IPtyWebSocket"/>
/// replays recorded messages, the same way <see cref="ServerSentEventReaderBenchmarks"/> replays
/// recorded SSE frames over a canned stream. The complete rung drives
/// <see cref="PtySession.ReadAsync"/> end to end (receive, cross-receive reassembly above the
/// session's fixed 16 KiB buffer, decode, yield); the decode-alone rung isolates
/// <see cref="PtyFrameReader.Read"/> from the receive loop entirely, over the exact assembled
/// messages the complete rung decodes. Small output frames stay within one receive; large ones
/// exceed it and exercise the reassembler; the control-frame case isolates the binary cursor path
/// from ordinary text output instead of diluting it into a mixed average.
/// </summary>
[MemoryDiagnoser]
public class PtySessionReadBenchmarks : IAsyncDisposable
{
    private const byte ControlFrameMarker = 0x00;
    private const long ExpectedCursor = 123456;
    private const string CursorFixtureName = "cursor-x1";
    private const int SmallFrameCount = 1024;
    private const int LargeFrameCount = 64;
    private const int LargeFramePayloadBytes = 40 * 1024;

    private static readonly IReadOnlyList<(WireFixture Fixture, (WebSocketMessageType Type, byte[] Payload)[] Messages)> Cases =
    [
        CursorCase(),
        OutputCase("output-small-x1024", SmallOutputLine(), SmallFrameCount),
        OutputCase("output-large-x64", LargeOutputPayload(), LargeFrameCount),
    ];

    private (WebSocketMessageType Type, byte[] Payload)[] _messages = [];
    private CannedPtyWebSocket? _socket;
    private PtySession? _session;

    public static IEnumerable<WireFixture> Fixtures() => Cases.Select(static entry => entry.Fixture);

    [ParamsSource(nameof(Fixtures))]
    public WireFixture Fixture { get; set; } = null!;

    /// <summary>
    /// Builds the one session and canned socket this fixture's benchmark invocations replay
    /// against; <see cref="CannedPtyWebSocket.Reset"/> rewinds it before every read instead of
    /// paying for a fresh socket and session per iteration, which would count construction
    /// noise as part of the read path this class isolates.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _messages = MessagesFor(Fixture);
        _socket = new CannedPtyWebSocket(_messages);
        _session = new PtySession(_socket);

        var delivered = await CollectFramesAsync(_session).ConfigureAwait(false);
        if (delivered.Count != Fixture.Items)
        {
            throw new InvalidOperationException($"Fixture '{Fixture.Name}' did not deliver the expected frame count.");
        }

        if (string.Equals(Fixture.Name, CursorFixtureName, StringComparison.Ordinal))
        {
            if (delivered is not [PtyCursorFrame { Cursor: ExpectedCursor }])
            {
                throw new InvalidOperationException("The cursor fixture did not decode the expected control frame.");
            }
        }
        else if (delivered.Any(static frame => frame is not PtyOutputFrame { Text.Length: > 0 }))
        {
            throw new InvalidOperationException($"Fixture '{Fixture.Name}' did not decode into complete, non-empty output frames.");
        }

        _socket.Reset();

        if (DecodeFrames() != Fixture.Items)
        {
            throw new InvalidOperationException($"Fixture '{Fixture.Name}' did not decode-alone into the expected frame count.");
        }
    }

    /// <summary>The complete read path: canned receive, cross-receive reassembly, decode, and yield.</summary>
    [Benchmark]
    public Task<int> ReadFramesAsync()
    {
        _socket!.Reset();
        return CountFramesAsync(_session!);
    }

    /// <summary>Decode alone, over the exact messages the complete rung assembles: no socket, no receive loop.</summary>
    [Benchmark]
    public int DecodeFrames()
    {
        var frames = 0;
        foreach (var (type, payload) in _messages)
        {
            _ = PtyFrameReader.Read(type, payload, payload.Length);
            frames++;
        }

        return frames;
    }

    [GlobalCleanup]
    public ValueTask CleanupAsync() => DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }

        _socket?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static async Task<int> CountFramesAsync(PtySession session)
    {
        var frames = 0;
        await foreach (var frame in session.ReadAsync().ConfigureAwait(false))
        {
            _ = frame;
            frames++;
        }

        return frames;
    }

    private static async Task<List<PtyFrame>> CollectFramesAsync(PtySession session)
    {
        var frames = new List<PtyFrame>();
        await foreach (var frame in session.ReadAsync().ConfigureAwait(false))
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static (WebSocketMessageType Type, byte[] Payload)[] MessagesFor(WireFixture fixture) =>
        Cases.First(entry => string.Equals(entry.Fixture.Name, fixture.Name, StringComparison.Ordinal)).Messages;

    private static (WireFixture Fixture, (WebSocketMessageType Type, byte[] Payload)[] Messages) CursorCase()
    {
        var payload = CursorFramePayload();
        var fixture = new WireFixture(CursorFixtureName, payload, items: 1, payloadBytesPerItem: payload.Length);
        return (fixture, [(WebSocketMessageType.Binary, payload)]);
    }

    private static (WireFixture Fixture, (WebSocketMessageType Type, byte[] Payload)[] Messages) OutputCase(string name, byte[] payload, int count)
    {
        var messages = new (WebSocketMessageType, byte[])[count];
        for (var index = 0; index < count; index++)
        {
            messages[index] = (WebSocketMessageType.Text, payload);
        }

        var fixture = new WireFixture(name, Repeat(payload, count), count, payloadBytesPerItem: payload.Length);
        return (fixture, messages);
    }

    /// <summary>The binary control frame: the marker byte followed by the cursor JSON body.</summary>
    private static byte[] CursorFramePayload() => [ControlFrameMarker, .. Encoding.UTF8.GetBytes($"{{\"cursor\":{ExpectedCursor}}}")];

    /// <summary>A small realistic terminal line, the size the live feed carries most of.</summary>
    private static byte[] SmallOutputLine() => "$ echo hello\r\nhello\r\n"u8.ToArray();

    /// <summary>
    /// A large output chunk sized to exceed <see cref="PtySession"/>'s fixed 16 KiB receive
    /// buffer, so replaying it forces exactly the cross-receive reassembly a real large replay
    /// chunk would.
    /// </summary>
    private static byte[] LargeOutputPayload()
    {
        const string line = "the quick brown fox jumps over the lazy dog\r\n";
        var builder = new StringBuilder(LargeFramePayloadBytes + line.Length);
        while (builder.Length < LargeFramePayloadBytes)
        {
            _ = builder.Append(line);
        }

        return Encoding.UTF8.GetBytes(builder.ToString(0, LargeFramePayloadBytes));
    }

    /// <summary>Concatenates one payload repeated <paramref name="count"/> times, for the fixture's reported wire bytes.</summary>
    private static byte[] Repeat(byte[] payload, int count)
    {
        var body = new byte[payload.Length * count];
        for (var index = 0; index < count; index++)
        {
            Array.Copy(payload, 0, body, index * payload.Length, payload.Length);
        }

        return body;
    }
}
