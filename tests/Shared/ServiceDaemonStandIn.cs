using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// One daemon-role stand-in of the isolated service fixture (<c>contender-probe stall</c> or
/// <c>contender-probe stale</c>), started and owned by a test: a real process of this machine whose
/// ready line names its pid and the loopback port it bound, so a test can seed a registration for
/// it and let the Ensure loop probe — and, for the stall, terminate — it. Disposal ends the process.
/// </summary>
internal sealed class ServiceDaemonStandIn : IAsyncDisposable
{
    private static readonly TimeSpan DisposalBound = TimeSpan.FromSeconds(15);

    private readonly Process _process;
    private int _disposed;

    private ServiceDaemonStandIn(Process process, int port)
    {
        _process = process;
        Port = port;
    }

    /// <summary>Gets the pid the operating system gave the stand-in.</summary>
    public int ProcessId => _process.Id;

    /// <summary>Gets the loopback port the stand-in bound and reported.</summary>
    public int Port { get; }

    /// <summary>Gets the endpoint the registration should name: <c>http://127.0.0.1:&lt;port&gt;</c>.</summary>
    public Uri Endpoint => new("http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture));

    /// <summary>Gets a value indicating whether the process has exited.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Starts one daemon stand-in and waits for its ready line naming pid and port.</summary>
    /// <param name="fileSystem">The filesystem the fixture build is resolved through.</param>
    /// <param name="mode">The stand-in mode: <c>stall</c> or <c>stale</c>.</param>
    /// <param name="cancellationToken">The caller's bound; the child is killed when it is cancelled before readiness.</param>
    /// <returns>The owned stand-in.</returns>
    public static async Task<ServiceDaemonStandIn> StartAsync(IFileSystem fileSystem, string mode, CancellationToken cancellationToken)
    {
        var command = new ServiceFixtureCommand(fileSystem).Resolve();
        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            Arguments = ProcessArgumentComposer.Compose([.. command.Skip(1), "contender-probe", mode]),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Starting '{startInfo.FileName}' returned no process.");
        try
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (!TryParsePort(line, out var port))
            {
                throw new InvalidOperationException(
                    $"The fixture mode '{mode}' printed '{line}' instead of a ready line naming a port.");
            }

            return new ServiceDaemonStandIn(process, port);
        }
        catch
        {
            _ = ProcessTreeTerminator.TryKill(process);
            process.Dispose();
            throw;
        }
    }

    /// <summary>Waits at most <paramref name="bound"/> for the process to exit.</summary>
    /// <param name="bound">The most this call waits.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>True when the process exited inside the bound.</returns>
    public async Task<bool> ObserveExitWithinAsync(TimeSpan bound, CancellationToken cancellationToken)
    {
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        observation.CancelAfter(bound);
        try
        {
            await _process.WaitForExitAsync(observation.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _ = ProcessTreeTerminator.TryKill(_process);
                _ = await ObserveExitWithinAsync(DisposalBound, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    /// <summary>Reads the <c>ready pid=&lt;pid&gt; port=&lt;port&gt;</c> line's port.</summary>
    private static bool TryParsePort(string? line, out int port)
    {
        port = 0;
        const string marker = " port=";
        if (line is null)
        {
            return false;
        }

        var index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var start = index + marker.Length;
        var end = start;
        while (end < line.Length && line[end] is >= '0' and <= '9')
        {
            end++;
        }

        if (end == start)
        {
            return false;
        }

        var parsed = 0;
        for (var position = start; position < end; position++)
        {
            parsed = checked((parsed * 10) + (line[position] - '0'));
        }

        port = parsed;
        return true;
    }
}
