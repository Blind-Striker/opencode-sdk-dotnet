using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OpenCode.Sdk.ServiceFixture;

/// <summary>
/// The <c>contender-probe</c> mode: a stand-in for the background service the pinned client's
/// Ensure loop spawns while a registration is unresolved (<c>spawnServiceContender</c>,
/// <c>packages/client/src/service-contender.ts</c> at the pin), started by a test through the SDK's
/// contender spawner seam. The spawner pipes only this process's stderr and gives the other two
/// standard streams nothing to do, so every observation rides stderr.
/// </summary>
/// <remarks>
/// Three probes. <c>echo-argv-env &lt;name&gt;…</c> floods stdout with <see cref="StdoutFloodBytes"/>
/// bytes, probes whether stdin reads end at once, and then prints one JSON line on stderr carrying
/// this process's own arguments (everything after the mode name) and the value of each named
/// environment variable (<c>null</c> for one that is absent), together with what the process can
/// see of its standard streams; it exits 0. <c>stderr-fill [bytes]</c> writes <c>bytes</c>
/// (16 KiB by default) of a position-numbered, whitespace-free pattern to stderr in 4 KiB chunks
/// paced five milliseconds apart, and exits 0. <c>daemon-sleep</c> prints
/// <c>ready pid=&lt;pid&gt;</c> on stderr and then sleeps until a signal ends it. Nothing here
/// touches the SDK or the registration file: the mode is the contender the seam starts, never a
/// seam consumer, so this executable builds without the spawner.
/// </remarks>
internal static class ContenderProbe
{
    /// <summary>
    /// The echo probe's stdout flood: far over every pipe buffer, so a child whose stdout is a
    /// pipe nobody reads blocks inside the flood and never reaches its JSON line.
    /// </summary>
    public const int StdoutFloodBytes = 80 * 1024;

    /// <summary>stderr-fill's block: ten digits of the block number, then the filler. No whitespace anywhere, so a consumer's trim can never change the content.</summary>
    private const int BlockBytes = 64;

    /// <summary>The block filler after the number: 54 characters, the exact half of a block that is not its number.</summary>
    private const string BlockFiller = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_-";

    /// <summary>stderr-fill's default: 256 blocks, so the spawner's final-8 KiB tail cuts across numbered block boundaries.</summary>
    public const int DefaultFillBytes = 16 * 1024;

    private const int ChunkBytes = 4 * 1024;

    /// <summary>The pacing between stderr-fill chunks: fast enough for the tail proof, slow enough for the release-drain proof to stay inside its survival window.</summary>
    private static readonly TimeSpan ChunkDelay = TimeSpan.FromMilliseconds(5);

    /// <summary>The most the stdin probe waits before it reports the read as blocked.</summary>
    private static readonly TimeSpan StandardInputBound = TimeSpan.FromSeconds(2);

    public static Task<int> RunAsync(string mode, IReadOnlyList<string> arguments) =>
        mode switch
        {
            "echo-argv-env" => EchoArgumentsAndEnvironmentAsync(arguments),
            "stderr-fill" => FillStandardErrorAsync(arguments),
            "daemon-sleep" => SleepLikeADaemonAsync(),
            _ => UsageAsync(),
        };

    private static async Task<int> EchoArgumentsAndEnvironmentAsync(IReadOnlyList<string> names)
    {
        // The flood comes before the report: when the spawner gave this process an unread pipe
        // for stdout, the flood blocks here and the JSON never prints, which is the exact
        // failure the harness reads from the missing line.
        var flood = new byte[StdoutFloodBytes];
        Array.Fill(flood, (byte)'x');
        using (var standardOutput = Console.OpenStandardOutput())
        {
            await standardOutput.WriteAsync(flood, CancellationToken.None).ConfigureAwait(false);
            await standardOutput.FlushAsync().ConfigureAwait(false);
        }

        var stdin = await ProbeStandardInputAsync().ConfigureAwait(false);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            environment[name] = Environment.GetEnvironmentVariable(name);
        }

        var report = new
        {
            // The muxer names the fixture dll as argv[0], so the probe's own arguments begin
            // after [dll, contender-probe, echo-argv-env].
            argv = Environment.GetCommandLineArgs().Skip(3).ToArray(),
            env = environment,
            stdin,
            stdinIsRedirected = Console.IsInputRedirected,
            stdoutIsRedirected = Console.IsOutputRedirected,
            stdoutFlushed = StdoutFloodBytes,
        };
        await Console.Error.WriteLineAsync(JsonSerializer.Serialize(report)).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Reads one character of stdin behind the bound: a NUL stdin answers EOF at once, a pipe the
    /// spawner left open blocks until someone writes, and an inherited console blocks for input.
    /// The bound turns every block into the <c>blocked</c> report instead of a hang the test
    /// would meet only as a timeout.
    /// </summary>
    [SlopwatchSuppress(
        "SW004",
        "The delay is the probe's bound, not a wait-for-condition: a spawner-left-open stdin blocks forever and an inherited console blocks for input, so the bound turns both into the blocked report instead of a hang the test would meet only as a timeout.")]
    private static async Task<string> ProbeStandardInputAsync()
    {
        var read = Task.Run(Console.In.Read);
        var completed = await Task.WhenAny(read, Task.Delay(StandardInputBound)).ConfigureAwait(false);
        if (completed != read)
        {
            return "blocked";
        }

        return await read.ConfigureAwait(false) < 0 ? "eof" : "byte";
    }

    [SlopwatchSuppress(
        "SW004",
        "The delay paces the fill inside the test's survival window: unpaced output would still prove the tail, but the release-drain proof needs the writer alive across Release, and pipe backpressure alone does not schedule that.")]
    private static async Task<int> FillStandardErrorAsync(IReadOnlyList<string> arguments)
    {
        var bytes = DefaultFillBytes;
        if (arguments.Count > 0
            && (arguments.Count > 1
                || !int.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out bytes)
                || bytes <= 0))
        {
            return await UsageAsync().ConfigureAwait(false);
        }

        // Numbered 64-byte blocks, flushed in 4 KiB chunks five milliseconds apart: the numbers
        // make the final tail's boundary checkable byte for byte, and the pace keeps the release
        // proof from finishing before its survival window closes.
        var builder = new StringBuilder(ChunkBytes);
        var remaining = bytes;
        var block = 0;
        while (remaining > 0)
        {
            var count = Math.Min(BlockBytes, remaining);
            var text = block.ToString("D10", CultureInfo.InvariantCulture) + BlockFiller;
            _ = builder.Append(count == BlockBytes ? text : text.AsSpan(0, count));
            remaining -= count;
            block++;
            if (builder.Length >= ChunkBytes || remaining == 0)
            {
                await Console.Error.WriteAsync(builder).ConfigureAwait(false);
                _ = builder.Clear();
                await Console.Error.FlushAsync().ConfigureAwait(false);
                await Task.Delay(ChunkDelay).ConfigureAwait(false);
            }
        }

        return 0;
    }

    /// <summary>
    /// Prints the ready line naming this process's pid on the one channel the contender seam
    /// reads, then sleeps. No signal handler is registered, so a <c>SIGTERM</c> (and the hard
    /// kill Windows makes of it) ends the process with the runtime's default action.
    /// </summary>
    private static async Task<int> SleepLikeADaemonAsync()
    {
        await Console.Error
            .WriteLineAsync($"ready pid={Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await Console.Error.FlushAsync().ConfigureAwait(false);

        // A gate nobody opens: the process lingers until a signal ends it, which is the point.
        using var forever = new SemaphoreSlim(0);
        await forever.WaitAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> UsageAsync()
    {
        await Console.Error
            .WriteLineAsync("Usage: contender-probe echo-argv-env [name …] | stderr-fill [bytes] | daemon-sleep")
            .ConfigureAwait(false);
        return 2;
    }
}
