namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>Loads the canned wire payloads the benchmarks replay.</summary>
internal static class BenchmarkFixtures
{
    /// <summary>A deep assistant message: reasoning + text + two tool parts across four union levels.</summary>
    public static Task<byte[]> DeepAssistantMessageAsync() => ReadAsync("deep-assistant-message.json");

    /// <summary>The bare health payload the live server returns.</summary>
    public static byte[] HealthBody() => "{\"healthy\":true,\"version\":\"0.0.0-bench\",\"pid\":42}"u8.ToArray();

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

    private static async Task<byte[]> ReadAsync(string name)
    {
        var stream = typeof(BenchmarkFixtures).Assembly
                .GetManifestResourceStream($"OpenCode.Sdk.Performance.Tests.Fixtures.{name}")
            ?? throw new InvalidOperationException($"Embedded fixture '{name}' is missing.");
        await using (stream.ConfigureAwait(false))
        {
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            return buffer.ToArray();
        }
    }
}
