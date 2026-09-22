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
/// is the launcher's to make ready and to end; this type only owns the boundary. On disposal it
/// ends every contender the shim started (Unix records each pid), any process a test tracked, and
/// whatever is registered, waits for each to leave, and removes the run root — or keeps it, with
/// the daemon log's tail printed, when the test failed.
/// </summary>
internal sealed class EnsureServiceContext : IAsyncDisposable
{
    private const string Channel = "local";
    private const int RealDaemonPort = 49374;
    private const int LogTailLines = 40;
    private static readonly TimeSpan ExitBound = TimeSpan.FromSeconds(15);

    private readonly RealFileSystem _fileSystem = new();
    private readonly List<int> _processes = [];
    private TestRunRoot? _runRoot;
    private Dictionary<string, string>? _environment;
    private string? _registrationFile;
    private string? _shimDirectory;
    private string? _shimPath;
    private string? _contenderPidFile;
    private int _port;
    private int _disposed;
    private bool _keep;

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

    /// <summary>Keeps the run root, and prints the daemon log's tail, instead of removing it: the evidence a failed test needs.</summary>
    public void KeepForDiagnosis() => _keep = true;

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

        // Every contender the shim started (the losers Ensure released included), every tracked
        // pid, and — for a test that failed before it tracked its winner — whatever is registered.
        var processIds = new HashSet<int>(_processes);
        processIds.UnionWith(await ReadContenderPidsAsync().ConfigureAwait(false));
        if (await ReadRegisteredPidAsync().ConfigureAwait(false) is { } registered)
        {
            _ = processIds.Add(registered);
        }

        foreach (var processId in processIds)
        {
            KillIfRunning(processId);
        }

        // A late contender that booted after its run root was deleted recreates the XDG folders
        // without home/ and fails its chdir: wait for every one to leave before removing anything.
        foreach (var processId in processIds)
        {
            _ = await ProcessObservation.ObserveExitWithinAsync(processId, ExitBound, CancellationToken.None).ConfigureAwait(false);
        }

        var keep = _keep || string.Equals(
            System.Environment.GetEnvironmentVariable("OPENCODE_SDK_TESTS_KEEP_LOGS"),
            "1",
            StringComparison.Ordinal);
        if (keep)
        {
            Console.WriteLine("Ensure service context retained; run root: " + RunRoot);
            Console.WriteLine(await DaemonLogTailAsync().ConfigureAwait(false));
        }
        else
        {
            _runRoot?.Dispose();
        }

        _runRoot = null;
    }

    private async Task<IEnumerable<int>> ReadContenderPidsAsync()
    {
        if (_contenderPidFile is not { } file || !_fileSystem.File.Exists(file))
        {
            return [];
        }

        var text = await ReadSharedAsync(file).ConfigureAwait(false);
        return
        [
            .. text
                .Split('\n')
                .Select(static line => int.TryParse(line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : 0)
                .Where(static pid => pid > 0),
        ];
    }

    private async Task<int?> ReadRegisteredPidAsync()
    {
        if (_registrationFile is not { } file || !_fileSystem.File.Exists(file))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(await ReadSharedAsync(file).ConfigureAwait(false));
        return ServiceRegistrationReader.TryRead(bytes)?.ProcessId;
    }

    /// <summary>The newest daemon log's final lines, the evidence a failed election leaves.</summary>
    private async Task<string> DaemonLogTailAsync()
    {
        var logDirectory = _fileSystem.Path.Combine(_environment!["XDG_DATA_HOME"], "opencode", "log");
        var newest = _fileSystem.Directory.Exists(logDirectory)
            ? _fileSystem.Directory.GetFiles(logDirectory, "*.log").OrderByDescending(_fileSystem.File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (newest is null)
        {
            return "No daemon log under " + logDirectory;
        }

        var lines = (await ReadSharedAsync(newest).ConfigureAwait(false)).Split('\n');
        return "Daemon log tail (" + newest + "):" + System.Environment.NewLine
            + string.Join(System.Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - LogTailLines)));
    }

    private async Task<string> ReadSharedAsync(string path)
    {
        using var stream = _fileSystem.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
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
        _contenderPidFile = _fileSystem.Path.Combine(_runRoot.Path, "contenders.pid");
        _shimPath = await OpenCodeCommandShim.WriteAsync(_fileSystem, _shimDirectory, _contenderPidFile, cancellationToken)
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
