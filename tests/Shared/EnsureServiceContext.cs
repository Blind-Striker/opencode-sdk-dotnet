using System.Globalization;
using System.Text;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Internal.BackgroundService;
using OpenCode.Sdk.Tests.Support;
using Testably.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// The hermetic arrangement one Ensure live test builds on, per test rather than per session: the
/// isolated XDG roots, a reserved free loopback port (never the maintainer's real daemon's), an
/// empty config seed naming that port, the <c>local</c>-channel registration path, and the
/// forwarding <c>opencode</c> shim the Ensure loop's default command resolves. The spawned service
/// is the launcher's to make ready and to end; this type only owns the boundary and, on disposal,
/// ends any process a test tracked and removes the run root.
/// </summary>
internal sealed class EnsureServiceContext : IAsyncDisposable
{
    private const string Channel = "local";
    private const int RealDaemonPort = 49374;

    private readonly RealFileSystem _fileSystem = new();
    private readonly List<int> _processes = [];
    private TestRunRoot? _runRoot;
    private Dictionary<string, string>? _environment;
    private string? _registrationFile;
    private string? _shimDirectory;
    private string? _shimPath;
    private int _port;
    private int _disposed;

    private EnsureServiceContext()
    {
    }

    /// <summary>Builds and initializes one context.</summary>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The initialized context.</returns>
    public static async Task<EnsureServiceContext> CreateAsync(CancellationToken cancellationToken)
    {
        var context = new EnsureServiceContext();
        await context.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return context;
    }

    /// <summary>Gets the per-run root every global directory is redirected into.</summary>
    public string RunRoot => _runRoot!.Path;

    /// <summary>Gets the isolated roots the spawned service and any isolated fixture process read.</summary>
    public IReadOnlyDictionary<string, string> Environment => _environment!;

    /// <summary>Gets the <c>local</c>-channel registration path under the isolated state root.</summary>
    public string RegistrationFile => _registrationFile!;

    /// <summary>Gets the reserved loopback port the spawned service binds.</summary>
    public int Port => _port;

    /// <summary>Gets the directory holding the forwarding <c>opencode</c> shim.</summary>
    public string ShimDirectory => _shimDirectory!;

    /// <summary>Gets the shim's full path, for an explicit <see cref="OpenCodeServerEnsureOptions.Command"/>.</summary>
    public string ShimPath => _shimPath!;

    /// <summary>Records a spawned service pid this context ends on disposal.</summary>
    /// <param name="processId">The pid the registration published.</param>
    public void TrackProcess(int processId) => _processes.Add(processId);

    /// <summary>
    /// The environment an isolated fixture process needs: the isolated roots plus a PATH that puts
    /// the shim first, so the default <c>opencode serve --service</c> command resolves
    /// <c>opencode</c> to the forwarding shim rather than any installed CLI.
    /// </summary>
    public Dictionary<string, string?> IsolatedProcessEnvironment()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in Environment)
        {
            environment[pair.Key] = pair.Value;
        }

        var existing = System.Environment.GetEnvironmentVariable("PATH");
        environment["PATH"] = ShimDirectory + _fileSystem.Path.PathSeparator + existing;
        return environment;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        foreach (var processId in _processes)
        {
            KillIfRunning(processId);
        }

        // Backstop for a test that failed before it tracked its winner: whatever is registered
        // right now is this context's spawned service, ended here so no daemon leaks past a test.
        if (_registrationFile is { } registrationFile && _fileSystem.File.Exists(registrationFile))
        {
            using var stream = _fileSystem.FileStream.New(registrationFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, CancellationToken.None).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            if (ServiceRegistrationReader.TryRead(bytes) is { } registration)
            {
                KillIfRunning(registration.ProcessId);
            }
        }

        var keep = string.Equals(
            System.Environment.GetEnvironmentVariable("OPENCODE_SDK_TESTS_KEEP_LOGS"),
            "1",
            StringComparison.Ordinal);
        if (keep)
        {
            Console.WriteLine("Ensure service context retained; run root: " + RunRoot);
        }
        else
        {
            _runRoot?.Dispose();
        }

        _runRoot = null;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _runRoot = new TestRunRoot(_fileSystem);
        _environment = ServerIsolation.Environment(_fileSystem, _runRoot.Path);
        _port = ReservePort();
        _registrationFile = _fileSystem.Path.Combine(
            _environment["XDG_STATE_HOME"], "opencode", "service-" + Channel + ".json");
        await SeedConfigAsync(cancellationToken).ConfigureAwait(false);
        _shimDirectory = _runRoot.CreateSubdirectory("shim");
        _shimPath = await OpenCodeCommandShim.WriteAsync(_fileSystem, _shimDirectory, cancellationToken)
            .ConfigureAwait(false);
    }

    private static int ReservePort()
    {
        var port = LoopbackPortReservation.Reserve();
        if (port == RealDaemonPort)
        {
            throw new InvalidOperationException(
                $"The reserved loopback port is {RealDaemonPort.ToString(CultureInfo.InvariantCulture)}, the local "
                + "channel's default where the maintainer's real daemon lives; the environment is contaminated. "
                + "Refusing to run rather than touch it.");
        }

        return port;
    }

    private async Task SeedConfigAsync(CancellationToken cancellationToken)
    {
        // The channel's config file, the way `opencode service set port` would have written it:
        // the daemon reads the port from here, so a spawned contender binds this reserved port.
        var directory = _environment!["OPENCODE_CONFIG_DIR"];
        _ = _fileSystem.Directory.CreateDirectory(directory);
        var file = _fileSystem.Path.Combine(directory, "service-" + Channel + ".json");
        using var stream = _fileSystem.FileStream.New(file, FileMode.Create, FileAccess.Write, FileShare.None);
        var bytes = new UTF8Encoding(false).GetBytes("{\"port\":" + _port.ToString(CultureInfo.InvariantCulture) + "}\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    [SlopwatchSuppress(
        "SW003",
        "Best-effort teardown: the pid being gone is the state the test is after, so a GetProcessById ArgumentException is the success path, not a swallowed failure.")]
    private static void KillIfRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            _ = ProcessTreeTerminator.TryKill(process);
        }
        catch (ArgumentException)
        {
            // The pid being gone is the state teardown is after.
        }
    }
}
