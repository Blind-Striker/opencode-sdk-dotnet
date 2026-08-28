using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk;

/// <summary>
/// A running standalone opencode server owned by this process: started on port zero with its
/// own generated lease credential, held through an open stdin pipe, and ended by disposal —
/// stdin EOF first, then a bounded grace, then a forced tree kill. This working object is the
/// only owner: disposal ends exactly its own child, and the operating system closes the lease
/// even when the owner crashes before disposal runs. It never discovers or attaches to another
/// server, so coexistence with any running server is safe by construction.
/// </summary>
public class OpenCodeServer : IAsyncDisposable
{
    private const int StderrRetainedLines = 40;

    private static readonly TimeSpan ForcedExitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Bounds the diagnostics-only output drain on every failure path. A drain is best-effort
    /// only — it must never turn into the unbounded hang <see cref="FlushOutputDrainAsync"/>'s
    /// remarks describe, so a caller always regains control within this window regardless of
    /// what the child (or anything the child spawned) is still holding open.
    /// </summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly Process? _process;
    private readonly Uri? _endpoint;
    private readonly string? _password;
    private readonly TimeSpan _gracefulShutdownTimeout;
    private int _disposed;

    private OpenCodeServer(Process process, Uri endpoint, string password, TimeSpan gracefulShutdownTimeout)
    {
        _process = process;
        _endpoint = endpoint;
        _password = password;
        _gracefulShutdownTimeout = gracefulShutdownTimeout;
    }

    /// <summary>
    /// Initializes a processless instance for deterministic contract tests; friend-assembly
    /// test seam. Disposal has no child to end and the client door works normally.
    /// </summary>
    internal OpenCodeServer(Uri endpoint, string password)
    {
        _endpoint = endpoint;
        _password = password;
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected OpenCodeServer()
    {
    }

    /// <summary>Gets the endpoint the started server bound on port zero.</summary>
    public virtual Uri Endpoint => _endpoint ?? throw MockSeam.CreateError("OpenCodeServer", "Endpoint");

    /// <summary>Gets the basic-authentication username; the pinned server accepts only <c>opencode</c>.</summary>
    public virtual string Username => "opencode";

    /// <summary>Gets the generated lease credential this start injected into the child.</summary>
    public virtual string Password => _password ?? throw MockSeam.CreateError("OpenCodeServer", "Password");

    /// <summary>Gets the child process identifier, for diagnostics and process-truth assertions.</summary>
    public virtual int ProcessId =>
        (_process ?? throw MockSeam.CreateError("OpenCodeServer", "ProcessId")).Id;

    /// <summary>
    /// Starts a fresh private standalone server: spawns the command with <c>--stdio --port 0</c>
    /// appended and the generated lease credential in the child environment, then waits for the
    /// JSON readiness line. On any failure the child tree is ended before the method throws.
    /// </summary>
    /// <param name="options">The launch options; null uses the defaults.</param>
    /// <param name="cancellationToken">The cancellation token ending the wait for readiness.</param>
    /// <returns>The started server, disposed by the caller.</returns>
    /// <exception cref="ArgumentException">The options are unusable: an empty command, a blank command entry, a non-positive readiness timeout, or a negative grace.</exception>
    /// <exception cref="OpenCodeServerException">The process could not start, exited before readiness, timed out, or broke the readiness contract.</exception>
    public static async Task<OpenCodeServer> StartAsync(
        OpenCodeServerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new OpenCodeServerOptions();
        var command = SnapshotCommand(options);
        ValidateTimeouts(options);
        var readinessTimeout = options.ReadinessTimeout;
        var gracefulShutdownTimeout = options.GracefulShutdownTimeout;

        var password = GeneratePassword();

        // Ownership stays local until the very end: every failure path throws through this
        // try, and the finally is the single place that disposes the child on that path. The
        // local is nulled only once the new OpenCodeServer has taken ownership on success
        // (TransportPolicy.CreateOwnedHttpClient's handler-ownership idiom, mirrored here).
        Process? process = null;
        try
        {
            process = CreateProcess(command, options, password);
            var stderrGate = new object();
            var stderrTail = new Queue<string>(StderrRetainedLines);
            var readyLine = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            AttachOutputHandlers(process, readyLine, stderrGate, stderrTail);
            StartChildProcess(process, command[0]);

            var line = await WaitForReadyLineAsync(
                process, readyLine, readinessTimeout, stderrGate, stderrTail, cancellationToken).ConfigureAwait(false);
            if (!ServerReadyLine.TryParse(line, out var endpoint))
            {
                await EndStartupFailureAsync(process).ConfigureAwait(false);
                throw new OpenCodeServerException(
                    $"The server's first stdout line is not the JSON readiness contract: '{line}'.{DescribeStderr(stderrGate, stderrTail)}");
            }

            var started = new OpenCodeServer(process, endpoint, password, gracefulShutdownTimeout);
            process = null;
            return started;
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Builds a client for this server. The door owns the connection identity: the delegate
    /// receives identity-unset options, and setting Endpoint, Username, or Password there is
    /// refused. Behavior members such as <see cref="OpenCodeClientOptions.Location"/> configure
    /// freely. Every call builds a new client owning its transport; the caller disposes it.
    /// </summary>
    /// <param name="configure">Optional behavior configuration; identity members must stay unset.</param>
    /// <returns>A new client bound to this server's endpoint and lease credential.</returns>
    /// <exception cref="InvalidOperationException">The delegate set an identity member.</exception>
    public virtual OpenCodeClient CreateClient(Action<OpenCodeClientOptions>? configure = null)
    {
        var endpoint = Endpoint;
        var password = Password;
        var options = new OpenCodeClientOptions();
        configure?.Invoke(options);
        if (options.Endpoint is not null ||
            options.Password is not null ||
            !string.Equals(options.Username, "opencode", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "CreateClient owns the connection identity: leave Endpoint, Username, and Password unset — the started server supplies them. Configure behavior members such as Location only.");
        }

        // A distinct instance carries the door's identity into the client: the object the
        // delegate received stays identity-unset for as long as the caller keeps a reference to
        // it, rather than being mutated out from under them after the delegate returns.
        return new OpenCodeClient(new OpenCodeClientOptions
        {
            Endpoint = endpoint,
            Username = options.Username,
            Password = password,
            Location = options.Location,
        });
    }

    /// <summary>
    /// Ends the owned child: closes stdin (the ownership lease), waits the configured grace,
    /// then escalates to a forced tree kill. Idempotent, bounded, and quiet for a child that is
    /// already gone.
    /// </summary>
    /// <returns>A task that completes once the child is ended and released.</returns>
    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        GC.SuppressFinalize(this);
        if (_process is null)
        {
            return;
        }

        try
        {
            await EndOwnedChildAsync(_process, _gracefulShutdownTimeout).ConfigureAwait(false);
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static void ValidateTimeouts(OpenCodeServerOptions options)
    {
        if (options.ReadinessTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("OpenCodeServerOptions.ReadinessTimeout must be positive.", nameof(options));
        }

        if (options.GracefulShutdownTimeout < TimeSpan.Zero)
        {
            throw new ArgumentException("OpenCodeServerOptions.GracefulShutdownTimeout cannot be negative.", nameof(options));
        }
    }

    private static void AttachOutputHandlers(
        Process process,
        TaskCompletionSource<string> readyLine,
        object stderrGate,
        Queue<string> stderrTail)
    {
        process.OutputDataReceived += (_, received) =>
        {
            // Continuous drain: the first line is the readiness contract; every later stdout
            // write is read and dropped so a chatty server can never fill the pipe and wedge
            // the probe (Q148; the reference keeps draining too, standalone.ts:42).
            if (received.Data is not null)
            {
                readyLine.TrySetResult(received.Data);
            }
        };
        process.ErrorDataReceived += (_, received) =>
        {
            if (received.Data is null)
            {
                return;
            }

            lock (stderrGate)
            {
                stderrTail.Enqueue(received.Data);
                if (stderrTail.Count > StderrRetainedLines)
                {
                    stderrTail.Dequeue();
                }
            }
        };
    }

    private static void StartChildProcess(Process process, string executable)
    {
        try
        {
            _ = process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new OpenCodeServerException($"Failed to start the server command '{executable}'.", exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private static async Task<string> WaitForReadyLineAsync(
        Process process,
        TaskCompletionSource<string> readyLine,
        TimeSpan readinessTimeout,
        object stderrGate,
        Queue<string> stderrTail,
        CancellationToken cancellationToken)
    {
        var exit = process.WaitForExitAsync(CancellationToken.None);
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readiness.CancelAfter(readinessTimeout);
        try
        {
            _ = await Task.WhenAny(readyLine.Task, exit).WaitAsync(readiness.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            await EndStartupFailureAsync(process).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The server start was canceled.", exception, cancellationToken);
            }

            throw new OpenCodeServerException(
                $"The server did not report readiness within {readinessTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s.{DescribeStderr(stderrGate, stderrTail)}");
        }

        if (readyLine.Task.IsCompleted)
        {
            return await readyLine.Task.ConfigureAwait(false);
        }

        var exitCode = process.ExitCode;

        // The root already exited on its own, but a launcher shim (a .cmd/bun wrapper that
        // spawns the real server and exits) can leave live grandchildren holding the redirected
        // pipe handles open — which would make the drain below wait for an EOF that never comes.
        // Killing the tree first (best-effort, already swallowed by ProcessTreeTerminator) closes
        // that gap before the bounded drain runs.
        ProcessTreeTerminator.Kill(process);
        await FlushOutputDrainAsync(process).ConfigureAwait(false);
        throw new OpenCodeServerException(
            $"The server exited with code {exitCode.ToString(CultureInfo.InvariantCulture)} before reporting readiness.{DescribeStderr(stderrGate, stderrTail)}");
    }

    private static async Task EndOwnedChildAsync(Process process, TimeSpan grace)
    {
        if (HasExited(process))
        {
            return;
        }

        // Stdin EOF ends the scoped server lifetime (server-process.ts:167-171); closing the
        // redirected writer is the lease release.
        try
        {
            process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
            // The pipe is already gone with the process.
        }
        catch (IOException)
        {
            // A broken pipe reports the same fact: the child is already leaving.
        }

        using (var graceWindow = new CancellationTokenSource(grace))
        {
            try
            {
                await process.WaitForExitAsync(graceWindow.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                // The grace expired; escalate below.
            }
        }

        ProcessTreeTerminator.Kill(process);
        using var forcedWindow = new CancellationTokenSource(ForcedExitTimeout);
        try
        {
            await process.WaitForExitAsync(forcedWindow.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Bounded on purpose: the kill was issued, and a disposal never hangs the caller.
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static async Task EndStartupFailureAsync(Process process)
    {
        ProcessTreeTerminator.Kill(process);
        try
        {
            using var exitWindow = new CancellationTokenSource(ForcedExitTimeout);
            await process.WaitForExitAsync(exitWindow.Token).ConfigureAwait(false);
            await FlushOutputDrainAsync(process).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The bounded exit wait expired without the process ending; skip the drain rather
            // than wait on a process that may still be alive and writing.
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        catch (Win32Exception)
        {
            // Already gone or inaccessible; the outer disposal releases the handles either way.
        }
    }

    /// <summary>
    /// Drains the redirected output readers to EOF so the stderr tail a failure message quotes is
    /// complete, bounded by <see cref="DrainTimeout"/>. The parameterless
    /// <see cref="Process.WaitForExit()"/> is what actually performs the drain — Polyfill's
    /// downlevel <c>WaitForExitAsync</c> is Exited-event-only and the int-timeout overload of
    /// <c>WaitForExit</c> never drains either — but EOF on the redirected pipes arrives only when
    /// every process holding the write end closes it. The immediate child having already exited
    /// does not guarantee that: a launcher shim can leave live grandchildren holding those handles
    /// open, which would make an unbounded call here hang forever. The bound below is the
    /// guarantee instead; diagnostics are best-effort and never outrank returning to the caller.
    /// </summary>
    private static Task FlushOutputDrainAsync(Process process) =>
        BoundedDrain.RunAsync(() => WaitForExitBestEffort(process), DrainTimeout);

    private static void WaitForExitBestEffort(Process process)
    {
        try
        {
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // No process handle left to drain.
        }
        catch (Win32Exception)
        {
            // Best-effort diagnostics flushing only.
        }
    }

    private static string[] SnapshotCommand(OpenCodeServerOptions options)
    {
        var command = options.Command;
        if (command is not { Count: > 0 })
        {
            throw new ArgumentException(
                "OpenCodeServerOptions.Command needs the executable and its leading arguments.", nameof(options));
        }

        var snapshot = new string[command.Count];
        for (var index = 0; index < command.Count; index++)
        {
            var entry = command[index];
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw new ArgumentException("OpenCodeServerOptions.Command entries cannot be blank.", nameof(options));
            }

            snapshot[index] = entry;
        }

        return snapshot;
    }

    private static Process CreateProcess(string[] command, OpenCodeServerOptions options, string password)
    {
        var process = new Process();
        var startInfo = process.StartInfo;
        startInfo.FileName = command[0];
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        if (options.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = options.WorkingDirectory;
        }

        var arguments = command.Skip(1).Concat(["--stdio", "--port", "0"]);
#if NET
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
#else
        // ArgumentList does not exist downlevel; the composed string follows the MSVCRT rules.
        startInfo.Arguments = ProcessArgumentComposer.Compose(arguments);
#endif
        if (options.Environment is not null)
        {
            foreach (var entry in options.Environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }

        // The explicit entry wins over anything inherited or supplied: the child's lease
        // credential is always this start's own (upstream standalone.ts:23-26 posture; stdio
        // mode scrubs it from the env the server hands to tools, server-process.ts:69-71).
        startInfo.Environment["OPENCODE_PASSWORD"] = password;
        process.EnableRaisingEvents = true;
        return process;
    }

    private static string GeneratePassword()
    {
        var bytes = new byte[32];
        using var random = RandomNumberGenerator.Create();
        random.GetBytes(bytes);

        // base64url, mirroring the reference client's randomBytes(32).toString("base64url").
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string DescribeStderr(object gate, Queue<string> tail)
    {
        lock (gate)
        {
            return tail.Count == 0 ? string.Empty : string.Concat(" Recent stderr: ", string.Join(" | ", tail));
        }
    }
}
