namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>Loads the canned wire payloads the benchmarks replay.</summary>
internal static class BenchmarkFixtures
{
    /// <summary>
    /// A deep assistant message — reasoning + text + two tool parts across four union
    /// levels — in the compact single-line form the wire carries. Indentation would put
    /// bytes through every measurement that no server ever sends.
    /// </summary>
    public static Task<byte[]> DeepAssistantMessageAsync() => ReadAsync("deep-assistant-message.json");

    /// <summary>The bare health payload the live server returns.</summary>
    public static byte[] HealthBody() => "{\"healthy\":true,\"version\":\"0.0.0-bench\",\"pid\":42}"u8.ToArray();

    /// <summary>A small event body, the size the live feed carries most of.</summary>
    public static byte[] SessionIdleBody() =>
        "{\"type\":\"session.idle\",\"properties\":{\"sessionID\":\"ses_bench\"}}"u8.ToArray();

    /// <summary>Wraps a payload in the <c>{"data": ...}</c> success envelope.</summary>
    public static byte[] DataEnvelope(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return [.. "{\"data\":"u8, .. payload, .. "}"u8];
    }

    /// <summary>Wraps one payload in the cursor-list envelope as a single-item page.</summary>
    public static byte[] CursorListEnvelope(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return [.. "{\"data\":["u8, .. payload, .. "],\"cursor\":{\"next\":\"cur_bench_next\"}}"u8];
    }

    /// <summary>Frames one payload as a run of server-sent events, the shape a live stream delivers.</summary>
    public static byte[] EventStream(byte[] payload, int frames)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);

        using var buffer = new MemoryStream();
        for (var index = 0; index < frames; index++)
        {
            buffer.Write("data: "u8);
            buffer.Write(payload);
            buffer.Write("\n\n"u8);
        }

        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadAsync(string name)
    {
        var stream = typeof(BenchmarkFixtures).Assembly
                .GetManifestResourceStream($"OpenCode.Sdk.Performance.Tests.Fixtures.{name}")
            ?? throw new InvalidOperationException($"Embedded fixture '{name}' is missing.");
        await using (stream.ConfigureAwait(false))
        {
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            return WireShaped(buffer.ToArray(), name);
        }
    }

    /// <summary>
    /// Holds every fixture to the shape a server actually sends. Indentation puts whitespace
    /// bytes through each measurement and frames as one line of content in an event stream,
    /// so the numbers would describe the fixture rather than the code under test. The file's
    /// own trailing newline is framing, not payload, and comes off.
    /// </summary>
    private static byte[] WireShaped(byte[] payload, string name)
    {
        var end = payload.Length;
        while (end > 0 && payload[end - 1] is (byte)'\n' or (byte)'\r')
        {
            end--;
        }

        var trimmed = payload.AsSpan(0, end).ToArray();
        return Array.IndexOf(trimmed, (byte)'\n') < 0
            ? trimmed
            : throw new InvalidOperationException(
                $"Fixture '{name}' is not wire-shaped: the payload a server sends occupies one line.");
    }
}
