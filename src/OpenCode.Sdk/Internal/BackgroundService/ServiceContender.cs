using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// One detached Ensure contender: the pid the spawn reported, closed/error observation in the
/// shape of the pinned client's <c>close</c>/<c>error</c> events, and the final 8 KiB of stderr.
/// Closed means the stderr pipe reached end-of-stream — the process exited and every holder of
/// the write end went away — so a grandchild holding the pipe delays it exactly as upstream's
/// <c>close</c> event is delayed; it never means the process was reaped, only that its output is
/// complete. The contender never kills: <see cref="Release"/> stops retention while the
/// background drain keeps consuming until the pipe closes, and disposal closes handles without
/// signalling. Either way a released contender may still win the election it was started for.
/// </summary>
internal sealed partial class ServiceContender : IDisposable
{
    /// <summary>The pinned client's <c>stderrLimit</c> (<c>service-contender.ts</c>): 8 KiB.</summary>
    private const int StderrLimit = 8 * 1024;

    /// <summary>What a redacted handoff ticket reads as in diagnostics.</summary>
    private const string RedactedMarker = "[redacted]";

    /// <summary>Windows' still-running sentinel for <c>GetExitCodeProcess</c>.</summary>
    private const uint StillActive = 259;

    /// <summary>Return immediately rather than wait: the observation poll never blocks.</summary>
    private const int WaitNoHang = 1;

    /// <summary>The <c>waitpid</c> errno when the pid is not a live child of this process.</summary>
    private const int NoChild = 10;

    private readonly Lock _gate = new();
    private readonly Stream? _stderr;
    private readonly SafeProcessHandle? _process;
    private readonly string? _redact;
    private readonly Task _drain;
    private readonly LinkedList<byte[]> _tail = new();

    private int _buffered;
    private bool _released;
    private bool _endOfStderr;
    private bool _disposed;
    private bool _reaped;
    private Exception? _error;
    private int? _exitCode;
    private int? _signal;

    /// <summary>Gets the spawned pid. For a Windows batch shim this is the cmd.exe host, never the server: the election reads the service pid from the registration.</summary>
    public int ProcessId { get; }

    public ServiceContender(int processId, Stream stderr, SafeProcessHandle? process, string? redact)
    {
        ArgumentNullException.ThrowIfNull(stderr);

        ProcessId = processId;
        _stderr = stderr;
        _process = process;
        _redact = string.IsNullOrEmpty(redact) ? null : redact;

        // Hot until the first read suspends: the drain owns no caller, only the pipe, and every
        // fault it can see is folded into the error slot — nothing escapes unobserved.
        _drain = DrainStderrAsync();
    }

    /// <summary>
    /// Initializes a live observation handle with no process. Tests use this so the election loop
    /// can spawn, retain, and release contenders without a pipe or a pid; production spawn still
    /// uses the handle constructor.
    /// </summary>
    internal ServiceContender(int processId)
    {
        ProcessId = processId;
        _drain = Task.CompletedTask;
    }

    /// <summary>Gets the asynchronous observation failure, when the background drain faulted; a synchronous spawn failure throws from the spawner instead.</summary>
    public Exception? Error
    {
        get
        {
            lock (_gate)
            {
                return _error;
            }
        }
    }

    /// <summary>
    /// Gets whether the contender finished the way upstream's <c>contenderFinished</c> means it:
    /// an observation error, or end-of-stream on stderr. Each look also polls the exit once, so a
    /// reaped child never lingers as a zombie between election rounds.
    /// </summary>
    public bool Closed
    {
        get
        {
            lock (_gate)
            {
                PollExitLocked();
                return _error is not null || _endOfStderr;
            }
        }
    }

    /// <summary>Gets whether the contender finished: an error, or a closed stderr pipe.</summary>
    public bool IsFinished => Error is not null || Closed;

    /// <summary>
    /// Gets the process exit code once a poll has observed it, or null while the process has not
    /// been seen to exit. The election doubles the spawn delay only for a finished contender whose
    /// code is exactly 0, matching upstream's <c>item.child.exitCode === 0</c>.
    /// </summary>
    public int? ExitCode
    {
        get
        {
            lock (_gate)
            {
                PollExitLocked();
                return _exitCode;
            }
        }
    }

    /// <summary>Gets a value indicating whether <see cref="Release"/> has given the retention up.</summary>
    public bool IsReleased
    {
        get
        {
            lock (_gate)
            {
                return _released;
            }
        }
    }

    /// <summary>
    /// Gets the retained stderr tail as text: the final 8 KiB decoded as UTF-8, trimmed the way
    /// upstream trims, with the handoff ticket redacted when the spawn carried one. Empty once
    /// <see cref="Release"/> gave the retention up.
    /// </summary>
    public string Stderr
    {
        get
        {
            lock (_gate)
            {
                return RenderStderrLocked();
            }
        }
    }

    /// <summary>
    /// The pinned client's <c>contenderFailure</c>: the observation error as-is, else a nonzero
    /// exit or a terminating signal wrapped with the retained stderr tail. Null while the
    /// contender is still running or ended cleanly — including a closed pipe with no exit yet,
    /// which the election keeps polling.
    /// </summary>
    /// <returns>The failure, or null when there is none to report.</returns>
    public OpenCodeServerException? TryGetFailure()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (_error is not null)
            {
                return new OpenCodeServerException(
                    $"The background service contender (pid {ProcessId.ToString(CultureInfo.InvariantCulture)}) failed to start.",
                    _error);
            }

            PollExitLocked();
            if (_signal is { } signal)
            {
                return new OpenCodeServerException(WithStderrLocked(
                    $"The background service contender (pid {ProcessId.ToString(CultureInfo.InvariantCulture)}) terminated on signal {signal.ToString(CultureInfo.InvariantCulture)}."));
            }

            if (_exitCode is { } code && code != 0)
            {
                return new OpenCodeServerException(WithStderrLocked(
                    $"The background service contender (pid {ProcessId.ToString(CultureInfo.InvariantCulture)}) exited with code {code.ToString(CultureInfo.InvariantCulture)}."));
            }

            return null;
        }
    }

    /// <summary>
    /// Gives the retention up the way upstream's <c>release</c> does: the tail is dropped and
    /// nothing further is kept, but the background drain keeps consuming until the pipe closes so
    /// a full pipe can never wedge the contender. Never signals or kills.
    /// </summary>
    public void Release()
    {
        lock (_gate)
        {
            _released = true;
            _tail.Clear();
            _buffered = 0;
        }
    }

    /// <summary>Closes the pipe and process handles without signalling or killing; the drain faults quietly against the closed pipe.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Reads the retained tail as one string in arrival order. Truncation can split a UTF-8
    /// sequence at the 8 KiB edge, which decodes the way a streaming decode does — with
    /// replacement characters, the same compromise upstream's buffer-to-string makes.
    /// </summary>
    private string RenderStderrLocked()
    {
        if (_released || _buffered == 0)
        {
            return string.Empty;
        }

        var merged = new byte[_buffered];
        var offset = 0;
        foreach (var chunk in _tail)
        {
            Buffer.BlockCopy(chunk, 0, merged, offset, chunk.Length);
            offset += chunk.Length;
        }

        var text = Encoding.UTF8.GetString(merged, 0, merged.Length);
        if (_redact is { Length: > 0 } ticket)
        {
            text = RedactTicket(text, ticket);
        }

        return text.Trim();
    }

    /// <summary>
    /// Ordinal ticket redaction without the framework's comparison overload, which the downlevel
    /// targets do not carry: split at every occurrence and rejoin over the marker.
    /// </summary>
    private static string RedactTicket(string text, string ticket)
    {
        var redacted = new StringBuilder(text.Length);
        var remainder = text;
        while (remainder.IndexOf(ticket, StringComparison.Ordinal) is >= 0 and var index)
        {
            _ = redacted.Append(remainder, 0, index).Append(RedactedMarker);
            remainder = remainder[(index + ticket.Length)..];
        }

        _ = redacted.Append(remainder);
        return redacted.ToString();
    }

    private string WithStderrLocked(string message)
    {
        var tail = RenderStderrLocked();
        return tail.Length == 0 ? message : message + "\n" + tail;
    }

    /// <summary>Keeps the final 8 KiB of the pipe and discards the rest; a released contender still drains, into nothing.</summary>
    private void AppendTailLocked(byte[] buffer, int count)
    {
        if (_released)
        {
            return;
        }

        var copy = new byte[count];
        Buffer.BlockCopy(buffer, 0, copy, 0, count);
        _tail.AddLast(copy);
        _buffered += count;
        while (_buffered > StderrLimit && _tail.First is { } head)
        {
            var excess = _buffered - StderrLimit;
            if (head.Value.Length <= excess)
            {
                _tail.RemoveFirst();
                _buffered -= head.Value.Length;
            }
            else
            {
                var rest = new byte[head.Value.Length - excess];
                Buffer.BlockCopy(head.Value, excess, rest, 0, rest.Length);
                head.Value = rest;
                _buffered -= excess;
            }
        }
    }

    /// <summary>
    /// The one non-blocking exit look both the poll and the failure read share. Windows reads the
    /// process handle; Unix reaps through <c>waitpid</c> with <c>WNOHANG</c>, which is what keeps a
    /// finished contender from lingering as a zombie. An <c>ECHILD</c> means someone else already
    /// reaped the pid, so there is nothing left to poll.
    /// </summary>
    private void PollExitLocked()
    {
        if (_disposed)
        {
            return;
        }

        if (IsWindows)
        {
            if (_process is { IsInvalid: false, IsClosed: false } handle &&
                GetExitCodeProcess(handle, out var code) &&
                code != StillActive)
            {
                _exitCode ??= (int)code;
            }

            return;
        }

        if (_reaped)
        {
            return;
        }

        var waited = WaitPid(ProcessId, out var status, WaitNoHang);
        if (waited == ProcessId)
        {
            _reaped = true;

            // The wait-status macros inlined: no children stop under this poll (no WUNTRACED),
            // so zero low bits are an exit and anything but the stop sentinel is a signal.
            var nature = status & 0x7f;
            if (nature == 0)
            {
                _exitCode = (status >> 8) & 0xff;
            }
            else if (nature != 0x7f)
            {
                _signal = nature;
            }
        }
        else if (waited < 0 && Marshal.GetLastWin32Error() == NoChild)
        {
            _reaped = true;
        }
    }

    private async Task DrainStderrAsync()
    {
        try
        {
            // The pipe stream the spawner handed over: on Unix its reads wait on the runtime's
            // event loop rather than a thread; on Windows the anonymous pipe is synchronous, so a
            // pending read occupies a pool thread, as .NET's own redirected streams do. The drain
            // owns the stream from here and closes it at end-of-stream.
#if NET
            var stderr = _stderr!;
            await using (stderr.ConfigureAwait(false))
            {
                await DrainPipeAsync(stderr).ConfigureAwait(false);
            }
#else
            using var stderr = _stderr!;
            await DrainPipeAsync(stderr).ConfigureAwait(false);
#endif
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                // Disposal closes the pipe under the drain; that race is the drain ending, not a
                // contender failing.
                if (!_disposed)
                {
                    _error ??= exception;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                CaptureExitLocked();
                _endOfStderr = true;
            }
        }
    }

    /// <summary>Consumes the pipe to end-of-stream, retaining the final 8 KiB unless released.</summary>
    private async Task DrainPipeAsync(Stream stderr)
    {
        var buffer = new byte[4096];
        while (true)
        {
#if NET
            var read = await stderr.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
#else
            var read = await stderr.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#endif
            if (read == 0)
            {
                break;
            }

            lock (_gate)
            {
                AppendTailLocked(buffer, read);
            }
        }
    }

    /// <summary>
    /// The exit is almost always already knowable when the pipe closes — the writer leaves with
    /// the process — so one poll here reports the code without ever blocking the drain.
    /// </summary>
    private void CaptureExitLocked() => PollExitLocked();

    private void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (_gate)
        {
            // One last non-blocking look before the poll shuts: a contender released and
            // then disposed before its exit would otherwise linger as a zombie on Unix
            // no one ever reaps.
            PollExitLocked();
            _disposed = true;
        }

        _process?.Dispose();
        _stderr?.Dispose();

        // The drain's outcome is folded into the observation slots above; this keeps the stored
        // task itself observed too.
        _ = _drain;
    }

    private static bool IsWindows =>
#if NET
        OperatingSystem.IsWindows();
#else
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif

#if NET
    [LibraryImport("kernel32", EntryPoint = "GetExitCodeProcess", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int WaitPid(int pid, out int status, int options);
#else
    [DllImport("kernel32", EntryPoint = "GetExitCodeProcess", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int WaitPid(int pid, out int status, int options);
#endif
}
