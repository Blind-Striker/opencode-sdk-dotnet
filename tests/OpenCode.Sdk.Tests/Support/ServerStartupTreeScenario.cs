using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Starts the launcher against a cooperating Bun process tree and owns the startup and bounded
/// cleanup around the handshake's verified process handles.
/// </summary>
internal sealed class ServerStartupTreeScenario : IAsyncDisposable
{
    private const string InvalidLineMode = "invalid-line";
    private const string SilentMode = "silent";

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    private readonly ServerStartupTreeHandshake _handshake;
    private readonly Task<OpenCodeServer> _startup;
    private readonly CancellationTokenSource _startupCancellation;
    private int _disposed;

    private ServerStartupTreeScenario(
        string mode,
        TimeSpan readinessTimeout,
        CancellationToken cancellationToken)
    {
        _startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _handshake = new ServerStartupTreeHandshake(_startupCancellation.Token);
        _startup = OpenCodeServer.StartAsync(
            new OpenCodeServerOptions
            {
                Command = ["bun", "-e", new FixtureLoader().LoadText("Server.child-tree.js")],
                Environment = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["OPENCODE_SDK_TEST_TREE_PORT"] = _handshake.Port.ToString(CultureInfo.InvariantCulture),
                    ["OPENCODE_SDK_TEST_TREE_NONCE"] = _handshake.Nonce,
                    ["OPENCODE_SDK_TEST_TREE_MODE"] = mode,
                },
                ReadinessTimeout = readinessTimeout,
            },
            _startupCancellation.Token);
    }

    public Process ChildProcess => _handshake.ChildProcess;

    public Process RootProcess => _handshake.RootProcess;

    public static ServerStartupTreeScenario BeginInvalidLine(CancellationToken cancellationToken) =>
        new(InvalidLineMode, TimeSpan.FromMinutes(2), cancellationToken);

    public static ServerStartupTreeScenario BeginSilent(
        TimeSpan readinessTimeout,
        CancellationToken cancellationToken) =>
        new(SilentMode, readinessTimeout, cancellationToken);

    public Task ObserveAndAcknowledgeAsync(CancellationToken cancellationToken) =>
        _handshake.ObserveAndAcknowledgeAsync(cancellationToken);

    public async Task<OpenCodeServer> WaitForStartupAsync(CancellationToken cancellationToken)
    {
        var startup = _startup;
        return await startup.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        await _startupCancellation.CancelAsync();
        _handshake.Stop();
        try
        {
            await _handshake.AwaitCleanupAsync(cleanup.Token);
            await AwaitStartupCleanupAsync(cleanup.Token);
            await EndCapturedProcessAsync(_handshake.CapturedChildProcess, cleanup.Token);
            await EndCapturedProcessAsync(_handshake.CapturedRootProcess, cleanup.Token);
        }
        finally
        {
            _handshake.Dispose();
            _startupCancellation.Dispose();
        }
    }

    private async Task AwaitStartupCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startup = _startup;
            var server = await startup.WaitAsync(cancellationToken);
            await server.DisposeAsync();
        }
        catch (OperationCanceledException exception)
        {
            _ = exception;
        }
        catch (OpenCodeServerException exception)
        {
            _ = exception;
        }
    }

    private static async Task EndCapturedProcessAsync(Process? process, CancellationToken cancellationToken)
    {
        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill();
        }
        catch (InvalidOperationException exception)
        {
            _ = exception;
            return;
        }
        catch (Win32Exception exception)
        {
            _ = exception;
            return;
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            _ = exception;
        }
    }
}
