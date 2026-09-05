using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
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
        bool acknowledge,
        CancellationToken cancellationToken)
    {
        _startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _handshake = new ServerStartupTreeHandshake(acknowledge, _startupCancellation.Token);
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
        new(InvalidLineMode, TimeSpan.FromMinutes(2), acknowledge: true, cancellationToken);

    public static ServerStartupTreeScenario BeginHandshakeFailure(CancellationToken cancellationToken) =>
        new(SilentMode, TimeSpan.FromMinutes(2), acknowledge: false, cancellationToken);

    public static ServerStartupTreeScenario BeginSilent(
        TimeSpan readinessTimeout,
        CancellationToken cancellationToken) =>
        new(SilentMode, readinessTimeout, acknowledge: true, cancellationToken);

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

        var failures = new List<Exception>();
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await CaptureFailureAsync(_startupCancellation.CancelAsync, failures);
            CaptureFailure(_handshake.Stop, failures);
            await CaptureFailureAsync(
                () => _handshake.ObserveAndAcknowledgeAsync(cleanup.Token), failures);
            await CaptureFailureAsync(
                () => AwaitStartupCleanupAsync(cleanup.Token), failures);
            await CaptureFailureAsync(
                () => EndCapturedProcessAsync(_handshake.CapturedChildProcess, cleanup.Token), failures);
            await CaptureFailureAsync(
                () => EndCapturedProcessAsync(_handshake.CapturedRootProcess, cleanup.Token), failures);
        }
        finally
        {
            try
            {
                _handshake.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                _startupCancellation.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        ThrowCleanupFailures(failures);
    }

    private static void CaptureFailure(Action cleanup, List<Exception> failures)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async Task CaptureFailureAsync(Func<Task> cleanup, List<Exception> failures)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void ThrowCleanupFailures(List<Exception> failures)
    {
        if (failures.Count is 0)
        {
            return;
        }

        if (failures.Count is 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
            return;
        }

        throw new AggregateException("Multiple failures occurred while cleaning the startup tree scenario.", failures);
    }

    private async Task AwaitStartupCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startup = _startup;
            var server = await startup.WaitAsync(cancellationToken);
            await server.DisposeAsync();
        }
        catch (OperationCanceledException exception) when (_startup.IsCanceled)
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

        process.Kill();
        await process.WaitForExitAsync(cancellationToken);
    }
}
