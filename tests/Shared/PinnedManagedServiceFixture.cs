using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Internal.BackgroundService;
using OpenCode.Sdk.Tests.Support;
using Testably.Abstractions;
using TUnit.Core.Interfaces;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// The accepted pin's own background service, started once per test session as
/// <c>bun &lt;cli entry&gt; serve --service</c> under fully isolated roots: the XDG map plus a
/// redirected home (service mode changes into <c>global.home</c>), a reserved free port seeded
/// into the channel's config file (the <c>local</c> default may be the developer's own), and the
/// <c>local</c> channel a source run compiles. Readiness is a strict registration under the state
/// root plus an authenticated health answer that repeats the registered pid. Cleanup kills only the
/// process this fixture started. Consumers declare
/// <c>[ClassDataSource&lt;PinnedManagedServiceFixture&gt;(Shared = SharedType.PerTestSession)]</c>
/// and <c>[NotInParallel(ParallelConstraintKeys.ServerProcess)]</c>.
/// Fail-fast, never skip: a missing submodule, install, or bun surfaces as an instructive error.
/// </summary>
public sealed class PinnedManagedServiceFixture : IAsyncInitializer, IAsyncDisposable
{
    /// <summary>The channel a source run compiles (<c>packages/cli/src/version.ts</c> at the pin).</summary>
    public const string Channel = "local";

    private const int OutputTailLimit = 16 * 1024;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan HealthAttemptTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(15);

    private readonly RealFileSystem _fileSystem = new();
    private readonly StringBuilder _standardOutput = new();
    private readonly StringBuilder _standardError = new();
    private readonly Lock _outputLock = new();
    private TestRunRoot? _runRoot;
    private Process? _process;
    private Task? _drain;
    private Dictionary<string, string>? _environment;
    private string? _registrationFile;
    private Uri? _endpoint;
    private string? _version;
    private int _processId;
    private int _disposed;

    /// <summary>Gets the registration file the service published, under the isolated state root.</summary>
    public string RegistrationFile => _registrationFile ?? throw NotInitialized();

    /// <summary>Gets the endpoint the registration published.</summary>
    public Uri Endpoint => _endpoint ?? throw NotInitialized();

    /// <summary>Gets the version the service reports; a source run reports <c>local</c>.</summary>
    public string Version => _version ?? throw NotInitialized();

    /// <summary>Gets the service's own pid: the one the registration published and health repeated.</summary>
    public int ProcessId => _processId is 0 ? throw NotInitialized() : _processId;

    /// <summary>Gets the exact environment the service received, so an isolated process can discover it the same way.</summary>
    public IReadOnlyDictionary<string, string> Environment => _environment ?? throw NotInitialized();

    /// <summary>Gets the per-run root every global directory is redirected into.</summary>
    public string RunRoot => (_runRoot ?? throw NotInitialized()).Path;

    public async Task InitializeAsync()
    {
        _runRoot = new TestRunRoot(_fileSystem);
        var pinned = new PinnedServerCommand(_fileSystem);
        var command = pinned.Resolve();
        var workingDirectory = _fileSystem.Path.Combine(pinned.RepositoryRoot, "external", "opencode", "packages", "cli");
        _environment = ServerIsolation.HomeAwareEnvironment(_fileSystem, _runRoot.Path);
        _ = _fileSystem.Directory.CreateDirectory(_environment["OPENCODE_TEST_HOME"]);
        _registrationFile = _fileSystem.Path.Combine(_environment["XDG_STATE_HOME"], "opencode", "service-" + Channel + ".json");
        var port = LoopbackPortReservation.Reserve();
        await SeedConfigAsync(port).ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        ConfigureCommandLine(startInfo, command[0], [.. command.Skip(1), "--service"]);
        foreach (var pair in _environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        foreach (var name in ServerIsolation.Uninherited)
        {
            _ = startInfo.Environment.Remove(name);
        }

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Starting '{startInfo.FileName}' returned no process.");
        _drain = Task.WhenAll(
            DrainAsync(_process.StandardOutput, _standardOutput),
            DrainAsync(_process.StandardError, _standardError));
        try
        {
            await WaitForReadyAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }

        Console.WriteLine(
            $"Pinned managed service started: channel {Channel}; port {port.ToString(CultureInfo.InvariantCulture)}; "
            + $"pid {_processId.ToString(CultureInfo.InvariantCulture)}; version {_version}; registration {_registrationFile}.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        var failure = await StopAsync().ConfigureAwait(false);
        var keep = failure is not null || string.Equals(
            System.Environment.GetEnvironmentVariable("OPENCODE_SDK_TESTS_KEEP_LOGS"), "1", StringComparison.Ordinal);
        if (keep)
        {
            Console.WriteLine("Pinned managed service output retained; run root: " + RunRoot);
            Console.WriteLine(OutputTail());
        }
        else
        {
            _runRoot?.Dispose();
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static InvalidOperationException NotInitialized() => new("The managed service fixture has not initialized.");

    /// <summary>
    /// The launcher's own spawn shape: a batch shim runs under cmd.exe through the screened
    /// composer, anything else through the MSVCRT composer that exists on every target.
    /// </summary>
    private static void ConfigureCommandLine(ProcessStartInfo startInfo, string command, IReadOnlyList<string> arguments)
    {
        var executable = new ExecutableResolver(ExecutableSearchEnvironment.ForCurrentProcess()).Resolve(command);
        if (executable.IsBatchScript)
        {
            startInfo.FileName = BatchCommandLine.InterpreterPath;
            startInfo.Arguments = BatchCommandLine.Compose(executable.Path, arguments, launcherArguments: []);
            return;
        }

        startInfo.FileName = executable.Path;
        startInfo.Arguments = ProcessArgumentComposer.Compose(arguments);
    }

    private async Task SeedConfigAsync(int port)
    {
        // The channel's config file, the way `opencode service set port` would have written it:
        // the daemon reads the port from here and persists the password it generates beside it.
        var directory = _fileSystem.Path.Combine(Environment["XDG_CONFIG_HOME"], "opencode");
        _ = _fileSystem.Directory.CreateDirectory(directory);
        var file = _fileSystem.Path.Combine(directory, "service-" + Channel + ".json");
        var document = "{\"port\":" + port.ToString(CultureInfo.InvariantCulture) + "}\n";
        using var stream = _fileSystem.FileStream.New(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = Encoding.UTF8.GetBytes(document);
        await stream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
    }

    [SlopwatchSuppress(
        "SW004",
        "Readiness poll of another process: the daemon writes its registration and opens its listener on its own schedule, no OS API signals either event to this process, and upstream's own incumbent check retries the same way (Schedule.spaced 100 millis); the poll is bounded by the readiness deadline.")]
    private async Task WaitForReadyAsync()
    {
        var process = _process ?? throw NotInitialized();
        var deadline = DateTime.UtcNow + OwnedServerPolicy.ReadinessTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The managed service exited with code {process.ExitCode.ToString(CultureInfo.InvariantCulture)} "
                    + $"before it registered and answered health.{System.Environment.NewLine}{OutputTail()}");
            }

            var registration = await TryReadRegistrationAsync().ConfigureAwait(false);
            if (registration is { Password: not null } && await AnswersHealthAsync(registration).ConfigureAwait(false))
            {
                _endpoint = registration.Endpoint;
                _processId = registration.ProcessId;
                return;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"The managed service did not register at '{RegistrationFile}' and answer health within {OwnedServerPolicy.ReadinessTimeout}.{System.Environment.NewLine}{OutputTail()}");
    }

    private async Task<ServiceRegistration?> TryReadRegistrationAsync()
    {
        if (!_fileSystem.File.Exists(RegistrationFile))
        {
            return null;
        }

        try
        {
            using var stream = _fileSystem.FileStream.New(RegistrationFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            return ServiceRegistrationReader.TryRead(buffer.ToArray());
        }
        catch (IOException)
        {
            // The daemon renames its temp file into place; a torn read simply polls again.
            return null;
        }
    }

    private async Task<bool> AnswersHealthAsync(ServiceRegistration registration)
    {
        using var client = new OpenCodeClient(new OpenCodeClientOptions
        {
            Endpoint = registration.Endpoint,
            Password = registration.Password,
        });
        using var attempt = new CancellationTokenSource(HealthAttemptTimeout);
        try
        {
            var health = await client.GetHealthAsync(cancellationToken: attempt.Token).ConfigureAwait(false);
            if (!health.Health.Healthy || health.Health.Pid != registration.ProcessId)
            {
                return false;
            }

            _version = health.Health.Version;
            return true;
        }
        catch (OpenCodeException)
        {
            // 503 while booting, a refused connection before the listener is up, or 500 after a
            // boot failure that the deadline reports with the output tail: all "not yet".
            return false;
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<Exception?> StopAsync()
    {
        if (_process is not { } process)
        {
            return null;
        }

        try
        {
            if (!process.HasExited)
            {
                _ = ProcessTreeTerminator.TryKill(process);
                using var exit = new CancellationTokenSource(ExitTimeout);
                await process.WaitForExitAsync(exit.Token).ConfigureAwait(false);
            }

            if (_drain is { } drain)
            {
                using var drainBound = new CancellationTokenSource(ExitTimeout);
                await drain.WaitAsync(drainBound.Token).ConfigureAwait(false);
            }

            return null;
        }
        catch (OperationCanceledException exception)
        {
            return new InvalidOperationException(
                $"The managed service (pid {process.Id.ToString(CultureInfo.InvariantCulture)}) did not exit within {ExitTimeout} after the tree kill.", exception);
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    private async Task DrainAsync(StreamReader reader, StringBuilder sink)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lock (_outputLock)
            {
                if (sink.Length < OutputTailLimit)
                {
                    _ = sink.AppendLine(line);
                }
            }
        }
    }

    private string OutputTail()
    {
        lock (_outputLock)
        {
            return "--- stdout ---" + System.Environment.NewLine + _standardOutput
                + "--- stderr ---" + System.Environment.NewLine + _standardError;
        }
    }
}
