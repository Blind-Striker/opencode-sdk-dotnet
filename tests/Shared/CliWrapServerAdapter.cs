using System.Globalization;
using System.IO.Abstractions;
using System.Security.Cryptography;
using CliWrap;
using OpenCode.Sdk.Internal;

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

    /// <summary>
    /// Bounded well above the 40-line exception tail (<see cref="DescribeLogs"/>): the
    /// failure-path log write wants enough context to diagnose without letting a chatty
    /// PerTestSession-lifetime server grow these files unbounded.
    /// </summary>
    private const int RetainedLogLines = 500;

    private readonly TaskCompletionSource<object?> _stdinLease =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _firstLine =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _forceKill = new();
    private readonly Lock _logGate = new();
    private readonly Queue<string> _stdout = new();
    private readonly Queue<string> _stderr = new();
    private TimeSpan _gracefulShutdownTimeout = DefaultGracefulShutdownTimeout;
    private Task? _execution;
    private Uri? _endpoint;
    private string? _password;
    private int _processId;
    private int _disposed;

    private CliWrapServerAdapter()
    {
    }

    public Uri Endpoint => _endpoint ?? throw new InvalidOperationException("The adapter has not reached readiness.");

    public string Password => _password ?? throw new InvalidOperationException("The adapter has not started.");

    public int ProcessId => _processId;

    /// <summary>
    /// Starts the adapter and waits for readiness. <paramref name="onConstructed"/> runs
    /// synchronously the instant the adapter object exists - before the process is even spawned -
    /// so a caller can retain the reference regardless of whether this method later throws. That
    /// is what lets <see cref="PinnedOpenCodeServerFixture"/> write out the captured stdout/stderr
    /// on a startup failure: every failure path below still disposes the adapter itself before
    /// throwing (the child is always ended), but the object - and its log buffers - survive for
    /// the caller to inspect.
    /// </summary>
    public static async Task<CliWrapServerAdapter> StartAsync(
        IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        TimeSpan readinessTimeout,
        TimeSpan? gracefulShutdownTimeout = null,
        Action<CliWrapServerAdapter>? onConstructed = null,
        CancellationToken cancellationToken = default)
    {
        var adapter = new CliWrapServerAdapter
        {
            _password = GeneratePassword(),
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
            await adapter.DisposeAsync();
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The adapter start was canceled.", exception, cancellationToken);
            }

            throw new InvalidOperationException(
                $"The pinned server did not report readiness within {readinessTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s.{adapter.DescribeLogs()}");
        }

        if (!adapter._firstLine.Task.IsCompleted)
        {
            await adapter.DisposeAsync();
            throw new InvalidOperationException(
                $"The pinned server exited before reporting readiness.{adapter.DescribeLogs()}");
        }

        var line = await adapter._firstLine.Task;
        if (!ServerReadyLine.TryParse(line, out var endpoint))
        {
            await adapter.DisposeAsync();
            throw new InvalidOperationException(
                $"The pinned server's first stdout line is not the readiness contract: '{line}'.{adapter.DescribeLogs()}");
        }

        adapter._endpoint = endpoint;
        return adapter;
    }

    public string DescribeLogs()
    {
        lock (_logGate)
        {
            var tail = _stderr.Skip(Math.Max(0, _stderr.Count - 40));
            return _stderr.Count == 0
                ? string.Empty
                : string.Concat(" Recent stderr: ", string.Join(" | ", tail));
        }
    }

    public void WriteLogs(IFileSystem fileSystem, string directory)
    {
        _ = fileSystem.Directory.CreateDirectory(directory);
        lock (_logGate)
        {
            fileSystem.File.WriteAllLines(fileSystem.Path.Combine(directory, "stdout.log"), _stdout);
            fileSystem.File.WriteAllLines(fileSystem.Path.Combine(directory, "stderr.log"), _stderr);
        }
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
        if (_execution is not null)
        {
            // The grace is a bounded wait on the execution itself rather than a race against a
            // timer: the child exiting on its own is the condition, and the bound is only what
            // stops disposal waiting forever for it.
            if (!await TryAwaitExitAsync(_execution, _gracefulShutdownTimeout))
            {
                await _forceKill.CancelAsync();
            }

            // Bounded the same way as the graceful wait above: the kill was issued, but CliWrap's
            // own tree-kill confirming it is not guaranteed prompt, so this second wait gets its
            // own bound rather than trusting it unconditionally
            // (OpenCodeServer.EndOwnedChildAsync's double-layered escalation, mirrored here).
            _ = await TryAwaitExitAsync(_execution, ForcedExitTimeout);
        }

        _forceKill.Dispose();
    }

    /// <summary>
    /// Waits for the execution to finish inside a bound of its own.
    /// </summary>
    /// <returns>
    /// True when the child exited inside the bound; false when the bound expired with it still
    /// running, or when the execution reported the forced cancellation it was handed. The second
    /// wait discards this because there is no third escalation to reach for: the tree kill has
    /// already been issued, and a disposal must return to its caller regardless.
    /// </returns>
    private static async Task<bool> TryAwaitExitAsync(Task execution, TimeSpan bound)
    {
        try
        {
            await execution.WaitAsync(bound);
            return true;
        }
        catch (TimeoutException)
        {
            // The bound expired with the child still running.
            return false;
        }
        catch (OperationCanceledException)
        {
            // The forced teardown reporting through the cancellation token it was given.
            return false;
        }
    }

    private void OnOutputLine(string line)
    {
        _ = _firstLine.TrySetResult(line);
        lock (_logGate)
        {
            _stdout.Enqueue(line);
            if (_stdout.Count > RetainedLogLines)
            {
                _ = _stdout.Dequeue();
            }
        }
    }

    private void OnErrorLine(string line)
    {
        lock (_logGate)
        {
            _stderr.Enqueue(line);
            if (_stderr.Count > RetainedLogLines)
            {
                _ = _stderr.Dequeue();
            }
        }
    }

    private static string GeneratePassword()
    {
        var bytes = new byte[32];
        using var random = RandomNumberGenerator.Create();
        random.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
