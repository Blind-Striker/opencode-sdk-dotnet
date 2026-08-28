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
    private static readonly TimeSpan GracefulShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource<object?> _stdinLease =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _firstLine =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _forceKill = new();
    private readonly Lock _logGate = new();
    private readonly List<string> _stdout = [];
    private readonly List<string> _stderr = [];
    private Task? _execution;
    private Uri? _endpoint;
    private string? _password;
    private int _processId;

    private CliWrapServerAdapter()
    {
    }

    public Uri Endpoint => _endpoint ?? throw new InvalidOperationException("The adapter has not reached readiness.");

    public string Password => _password ?? throw new InvalidOperationException("The adapter has not started.");

    public int ProcessId => _processId;

    public static async Task<CliWrapServerAdapter> StartAsync(
        IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        TimeSpan readinessTimeout)
    {
        var adapter = new CliWrapServerAdapter
        {
            _password = GeneratePassword(),
        };
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
        using var timeout = new CancellationTokenSource(readinessTimeout);
        try
        {
            _ = await Task.WhenAny(adapter._firstLine.Task, execution.Task).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            await adapter.DisposeAsync();
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
        _ = _stdinLease.TrySetResult(null);
        if (_execution is not null)
        {
            var graceful = await Task.WhenAny(_execution, Task.Delay(GracefulShutdownTimeout, CancellationToken.None));
            if (graceful != _execution)
            {
                await _forceKill.CancelAsync();
            }

            try
            {
                await _execution;
            }
            catch (OperationCanceledException)
            {
                // The forced teardown reports through the cancellation it was given.
            }
        }

        _forceKill.Dispose();
    }

    private void OnOutputLine(string line)
    {
        _ = _firstLine.TrySetResult(line);
        lock (_logGate)
        {
            _stdout.Add(line);
        }
    }

    private void OnErrorLine(string line)
    {
        lock (_logGate)
        {
            _stderr.Add(line);
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
