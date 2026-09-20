using System.Diagnostics;
using System.IO.Abstractions;
using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// One lingering mode of the isolated service fixture (<c>idle</c> or <c>ignore-sigterm</c>),
/// started and owned by a test: a real process of this machine with a known pid, alive until the
/// test's subject ends it or disposal kills it. Readiness is the <c>ready</c> line the mode prints
/// once its stdout is open, so a snapshot taken after <see cref="StartAsync"/> returns names a
/// process that is fully started. Its stderr is collected the way the launcher collects a child's,
/// through the asynchronous read the process class drives, so nothing here holds a task over the
/// process's streams.
/// </summary>
internal sealed class ServiceFixtureProcess : IAsyncDisposable
{
    private const string ReadyLine = "ready";
    private static readonly TimeSpan DisposalBound = TimeSpan.FromSeconds(15);

    private readonly Process _process;
    private readonly List<string> _standardError = [];
    private readonly Lock _standardErrorLock = new();
    private int _disposed;

    private ServiceFixtureProcess(Process process)
    {
        _process = process;
        _process.ErrorDataReceived += (_, received) =>
        {
            if (received.Data is null)
            {
                return;
            }

            lock (_standardErrorLock)
            {
                _standardError.Add(received.Data);
            }
        };
        _process.BeginErrorReadLine();
    }

    /// <summary>Gets the pid the operating system gave the fixture process.</summary>
    public int ProcessId => _process.Id;

    /// <summary>Gets a value indicating whether the process has exited.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Starts one lingering mode and waits for its <c>ready</c> line.</summary>
    /// <param name="fileSystem">The filesystem the fixture build is resolved through.</param>
    /// <param name="mode">The fixture mode: <c>idle</c> or <c>ignore-sigterm</c>.</param>
    /// <param name="cancellationToken">The caller's bound; the child is killed when it is cancelled before readiness.</param>
    /// <returns>The owned process.</returns>
    public static async Task<ServiceFixtureProcess> StartAsync(IFileSystem fileSystem, string mode, CancellationToken cancellationToken)
    {
        var command = new ServiceFixtureCommand(fileSystem).Resolve();
        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            Arguments = ProcessArgumentComposer.Compose([.. command.Skip(1), mode]),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Starting '{startInfo.FileName}' returned no process.");
        var fixture = new ServiceFixtureProcess(process);
        try
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (!string.Equals(line, ReadyLine, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The fixture mode '{mode}' printed '{line}' instead of '{ReadyLine}'. {fixture.StandardErrorTail()}");
            }
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }

        return fixture;
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
            await _process.WaitForExitAsync(observation.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Ends the process when it is still running and releases it; idempotent, so a test may end
    /// the process early and still leave the <c>await using</c> in place. A process that leaks
    /// past the test inherits the test host's console handles and keeps <c>dotnet test</c> alive
    /// after the host exits, which is why every start sits inside an <c>await using</c>.
    /// </summary>
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
                _ = await ObserveExitWithinAsync(DisposalBound, CancellationToken.None);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private string StandardErrorTail()
    {
        lock (_standardErrorLock)
        {
            return string.Join(Environment.NewLine, _standardError);
        }
    }
}
