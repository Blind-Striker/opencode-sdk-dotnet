using System.Globalization;
using System.IO.Abstractions;
using System.Security.Cryptography;
using CliWrap;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.TestSupport.Ownership;
using OpenCode.Sdk.TestSupport.Ownership.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Test-only control adapter over the pinned server's stdio contract (design §7.2): spawns the
/// command with the lease credential injected, drains both streams continuously through
/// pull-based line delegates, resolves readiness from the first stdout line via the product's
/// own <see cref="ServerReadyLine"/> contract, holds stdin open as the ownership lease, and
/// tears down stdin-EOF → bounded wait → forced kill. It separates launcher failures from
/// SDK/server agreement; production scenarios dogfood <see cref="OpenCodeServer"/> instead
/// (design §7.3). CliWrap is a repo-test dependency only (ADR-0001).
/// </summary>
internal sealed class CliWrapServerAdapter : IAsyncDisposable
{
    private static readonly TimeSpan DefaultGracefulShutdownTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Mirrors OpenCodeServer's own ForcedExitTimeout: the bound the escalation itself gets, so a
    /// tree-kill CliWrap cannot confirm promptly never turns disposal into an unbounded hang.
    /// </summary>
    private static readonly TimeSpan ForcedExitTimeout = TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource<object?> _stdinLease =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _firstLine =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _forceKill = new();
    private readonly Lock _logGate = new();
    private readonly ServerLogTail _stdout = new();
    private readonly ServerLogTail _stderr = new();
    private TaskCompletionSource<object?> _stderrChanged = NewSignal();
    private TimeSpan _gracefulShutdownTimeout = DefaultGracefulShutdownTimeout;
    private Task? _execution;
    private Uri? _endpoint;
    private string? _password;
    private int _processId;
    private int _disposed;
    private IOwnedOperationDeadline _deadline = new OwnedOperationDeadline();

    private CliWrapServerAdapter()
    {
    }

    public Uri Endpoint => _endpoint ?? throw new InvalidOperationException("The adapter has not reached readiness.");

    public string Password => _password ?? throw new InvalidOperationException("The adapter has not started.");

    public int ProcessId => _processId;

    public LateCleanupFailureReport? LateFailures { get; private set; }

    /// <summary>
    /// Starts the adapter and waits for readiness. <paramref name="onConstructed"/> runs
    /// synchronously the instant the adapter object exists - before the process is even spawned -
    /// so a caller can retain the reference regardless of whether this method later throws. That
    /// is what lets <see cref="PinnedOpenCodeServerFixture"/> write out the captured stdout/stderr
    /// on a startup failure: teardown is bounded, and the adapter retains its buffers and any
    /// pending execution until that work actually ends.
    /// </summary>
    public static async Task<CliWrapServerAdapter> StartAsync(
        IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        TimeSpan readinessTimeout,
        TimeSpan? gracefulShutdownTimeout = null,
        Action<CliWrapServerAdapter>? onConstructed = null,
        IOwnedOperationDeadline? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var adapter = new CliWrapServerAdapter
        {
            _password = GeneratePassword(),
            _deadline = deadline ?? new OwnedOperationDeadline(),
            _gracefulShutdownTimeout = gracefulShutdownTimeout ?? DefaultGracefulShutdownTimeout,
        };
        onConstructed?.Invoke(adapter);
        var cli = Cli.Wrap(command[0])
            .WithArguments([.. command.Skip(1), "--stdio", "--port", "0"])
            .WithWorkingDirectory(workingDirectory)
            .WithEnvironmentVariables(variables =>
            {
                foreach (var entry in environment)
                {
                    _ = variables.Set(entry.Key, entry.Value);
                }

                // The last write wins: the lease credential can never be shadowed.
                _ = variables.Set("OPENCODE_PASSWORD", adapter._password);
            })
            .WithStandardInputPipe(PipeSource.Create(async (_, cancellationToken) =>
            {
                // A pending source keeps the pipe open; completing the lease closes it (EOF).
                // (A single "_" parameter is a real, usable identifier in C# - only two or more
                // discard-named parameters bind as true discards - so it is never reassigned here.)
                using var closeOnKill = cancellationToken.Register(
                    () => adapter._stdinLease.TrySetResult(null));
                await adapter._stdinLease.Task.ConfigureAwait(false);
            }))
            .WithStandardOutputPipe(PipeTarget.ToDelegate(adapter.OnOutputLine))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(adapter.OnErrorLine))
            .WithValidation(CommandResultValidation.None);

        var execution = cli.ExecuteAsync(adapter._forceKill.Token);
        adapter._processId = execution.ProcessId;
        adapter._execution = execution.Task;

        // Linked so a caller-supplied cancellation (e.g. a test's own [Timeout]) and the internal
        // readiness bound both reach the same wait, distinguished below exactly as
        // OpenCodeServer.WaitForReadyLineAsync distinguishes the same two sources.
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readiness.CancelAfter(readinessTimeout);
        try
        {
            _ = await Task.WhenAny(adapter._firstLine.Task, execution.Task).WaitAsync(readiness.Token);
        }
        catch (OperationCanceledException exception)
        {
            var failure = cancellationToken.IsCancellationRequested
                ? (Exception)new OperationCanceledException("The adapter start was canceled.", exception, cancellationToken)
                : new InvalidOperationException(
                    $"The pinned server did not report readiness within {readinessTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s.");
            await adapter.ThrowAfterTeardownAsync(failure);
            throw;
        }

        if (!adapter._firstLine.Task.IsCompleted)
        {
            await adapter.ThrowAfterTeardownAsync(new InvalidOperationException(
                "The pinned server exited before reporting readiness."));
        }

        var line = await adapter._firstLine.Task;
        if (!ServerReadyLine.TryParse(line, out var endpoint))
        {
            await adapter.ThrowAfterTeardownAsync(new InvalidOperationException(
                $"The pinned server's first stdout line is not the readiness contract: '{line}'."));
        }

        adapter._endpoint = endpoint;
        return adapter;
    }

    public string DescribeLogs()
    {
        lock (_logGate)
        {
            var tail = _stderr.Describe();
            return tail.Length == 0 ? string.Empty : " Recent stderr: " + tail;
        }
    }

    public bool HasErrorLine(string fragment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment);
        lock (_logGate)
        {
            return _stderr.Find(fragment) is not null;
        }
    }

    public async Task<string> WaitForErrorLineAsync(string fragment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment);
        while (true)
        {
            Task signal;
            lock (_logGate)
            {
                var match = _stderr.Find(fragment);
                if (match is not null)
                {
                    return match;
                }

                signal = _stderrChanged.Task;
            }

            await signal.WaitAsync(cancellationToken);
        }
    }

    public ServerLogSnapshot SnapshotLogs()
    {
        lock (_logGate)
        {
            return new ServerLogSnapshot { StandardOutput = _stdout.Snapshot(), StandardError = _stderr.Snapshot() };
        }
    }

    public async Task WriteLogsAsync(IFileSystem fileSystem, string directory)
    {
        var snapshot = SnapshotLogs();
        _ = fileSystem.Directory.CreateDirectory(directory);
        var writer = new DiagnosticFileWriter(fileSystem);
        await writer.WriteAsync(fileSystem.Path.Combine(directory, "stdout.log"), string.Join(Environment.NewLine, snapshot.StandardOutput));
        await writer.WriteAsync(fileSystem.Path.Combine(directory, "stderr.log"), string.Join(Environment.NewLine, snapshot.StandardError));
    }

    public async ValueTask DisposeAsync()
    {
        // The onConstructed-captured reference (see StartAsync's doc comment) makes a second call
        // on the same adapter a normal occurrence on every init failure: once inside StartAsync's
        // own failure branch, once again from the caller's own teardown. Without this guard the
        // second call's forced-kill branch could run CancelAsync on the _forceKill token source
        // this same method already disposed at the end of the first call
        // (OpenCodeServer.DisposeAsync's own Interlocked guard, mirrored here).
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        _ = _stdinLease.TrySetResult(null);
        var cancellation = Task.CompletedTask;
        try
        {
            if (_execution is not null && !await TryAwaitExitAsync(_execution, _gracefulShutdownTimeout))
            {
                cancellation = Task.Run(_forceKill.CancelAsync, CancellationToken.None);
                var forced = new OwnedCleanup(ForcedExitTimeout, _deadline);
                forced.Own("pinned server forced cancellation", cancellation);
                forced.Own("pinned server forced exit", _execution, _ => _forceKill.IsCancellationRequested);
                try
                {
                    await forced.CompleteAsync(null);
                }
                finally
                {
                    LateFailures = forced.LateFailures;
                }
            }
        }
        finally
        {
            // A timed-out fixture still owns the execution and cancellation. Release the source
            // only when neither can use it; the continuation also observes a late execution fault.
            var pending = Task.WhenAll(_execution ?? Task.CompletedTask, cancellation);
            if (pending.IsCompleted)
            {
                _ = pending.Exception;
                _forceKill.Dispose();
            }
            else
            {
                _ = pending.ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        _forceKill.Dispose();
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// Waits for the execution to finish inside a bound of its own.
    /// </summary>
    /// <returns>
    /// True when execution succeeds inside the bound; false when its observation deadline wins.
    /// An execution failure remains distinct from a deadline failure.
    /// </returns>
    private async Task<bool> TryAwaitExitAsync(Task execution, TimeSpan bound)
    {
        try
        {
            await _deadline.WaitAsync("pinned server graceful exit", execution, bound, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            return false;
        }

        await execution;
        return true;
    }

    private async Task ThrowAfterTeardownAsync(Exception primary)
    {
        var cleanup = new OwnedCleanup(TimeSpan.FromSeconds(25), _deadline);
        cleanup.Own("pinned server startup teardown", async _ => await DisposeAsync());
        try
        {
            await cleanup.CompleteAsync(primary);
        }
        catch (Exception exception) when (ReferenceEquals(exception, primary))
        {
            primary.Data["PinnedServer.StartupLogs"] = DescribeLogs();
            throw;
        }
    }
    private void OnOutputLine(string line)
    {
        _ = _firstLine.TrySetResult(line);
        lock (_logGate)
        {
            _stdout.Append(line);
        }
    }

    private void OnErrorLine(string line)
    {
        TaskCompletionSource<object?> changed;
        lock (_logGate)
        {
            _stderr.Append(line);

            changed = _stderrChanged;
            _stderrChanged = NewSignal();
        }

        _ = changed.TrySetResult(null);
    }

    private static TaskCompletionSource<object?> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string GeneratePassword()
    {
        var bytes = new byte[32];
        using var random = RandomNumberGenerator.Create();
        random.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
