using Testably.Abstractions;
using TUnit.Core.Interfaces;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// The exact-pin server fixture (design §7.2): one pinned server per test session over the
/// CliWrap control adapter, per-run home/state isolation, isolated workspaces, and logs
/// retained on failure. Consumers declare
/// <c>[ClassDataSource&lt;PinnedOpenCodeServerFixture&gt;(Shared = SharedType.PerTestSession)]</c>;
/// process-global scenarios add <c>[NotInParallel("pinned-opencode-server")]</c>.
/// Fail-fast, never skip: a missing submodule/install/bun surfaces as an instructive error.
/// </summary>
public sealed class PinnedOpenCodeServerFixture : IAsyncInitializer, IAsyncDisposable
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(3);

    private readonly RealFileSystem _fileSystem = new();
    private readonly IReadOnlyList<string>? _commandOverride;
    private readonly string? _workingDirectoryOverride;
    private CliWrapServerAdapter? _adapter;
    private TestRunRoot? _runRoot;
    private bool _retainLogs;

    public PinnedOpenCodeServerFixture()
    {
    }

    /// <summary>
    /// Test-only seam: drives <see cref="InitializeAsync"/>/<see cref="DisposeAsync"/> against an
    /// injected command instead of the real pinned server, so a deliberate startup failure can be
    /// forced through this fixture itself - pinning the failure-path log-retention contract rather
    /// than only exercising it through the lower-level adapter.
    /// </summary>
    internal PinnedOpenCodeServerFixture(IReadOnlyList<string> command, string workingDirectory)
    {
        _commandOverride = command;
        _workingDirectoryOverride = workingDirectory;
    }

    public Uri Endpoint => Adapter.Endpoint;

    internal CliWrapServerAdapter Adapter =>
        _adapter ?? throw new InvalidOperationException("The fixture has not initialized.");

    internal TestRunRoot RunRoot =>
        _runRoot ?? throw new InvalidOperationException("The fixture has not initialized.");

    public async Task InitializeAsync()
    {
        _runRoot = new TestRunRoot(_fileSystem);
        IReadOnlyList<string> command;
        string workingDirectory;
        if (_commandOverride is not null && _workingDirectoryOverride is not null)
        {
            command = _commandOverride;
            workingDirectory = _workingDirectoryOverride;
        }
        else
        {
            var pinnedCommand = new PinnedServerCommand(_fileSystem);
            command = pinnedCommand.Resolve();

            // Bun's workspace/tsconfig discovery for the pinned monorepo's JSX packages walks
            // from the process's working directory, not from the absolute entry-file path (Task
            // 2's confirmed repro, OpenCodeServerLifecycleTests.StartPinnedAsync): a scratch
            // directory outside the checkout leaves that discovery unable to find the workspace
            // root, and the source-run server fails before readiness with "Cannot find module
            // 'react/jsx-dev-runtime'". Anchoring at the CLI package is what upstream's own "dev"
            // script does; state/data/cache/config stay isolated through the environment below
            // regardless of this directory.
            workingDirectory = _fileSystem.Path.Combine(
                pinnedCommand.RepositoryRoot, "external", "opencode", "packages", "cli");
        }

        try
        {
            _adapter = await CliWrapServerAdapter.StartAsync(
                command,
                BuildEnvironment(_runRoot.Path),
                workingDirectory,
                ReadinessTimeout,
                // Captured the instant the adapter object exists, regardless of whether StartAsync
                // later throws: a startup failure otherwise leaves _adapter null forever (StartAsync
                // disposes its own local on every failure path before throwing, but never hands it
                // back through its return value), which made the WriteLogs call below unreachable
                // dead code on exactly the failure it exists for.
                onConstructed: created => _adapter = created);
        }
        catch
        {
            _retainLogs = true;
            throw;
        }
    }

    public OpenCodeClient CreateClient(LocationSelector? location = null) =>
        new(new OpenCodeClientOptions
        {
            Endpoint = Adapter.Endpoint,
            Password = Adapter.Password,
            Location = location,
        });

    public TestWorkspace CreateWorkspace() => new(_fileSystem, RunRoot.Path);

    public async ValueTask DisposeAsync()
    {
        var keep = _retainLogs ||
                   string.Equals(
                       Environment.GetEnvironmentVariable("OPENCODE_SDK_TESTS_KEEP_LOGS"),
                       "1",
                       StringComparison.Ordinal);
        if (_adapter is not null)
        {
            if (keep && _runRoot is not null)
            {
                _adapter.WriteLogs(_fileSystem, _fileSystem.Path.Combine(_runRoot.Path, "logs"));
                Console.WriteLine($"Pinned server logs retained under: {_runRoot.Path}");
            }

            await _adapter.DisposeAsync();
        }

        if (!keep)
        {
            _runRoot?.Dispose();
        }
    }

    private Dictionary<string, string> BuildEnvironment(string runRoot) =>
        ServerIsolation.Environment(_fileSystem, runRoot);
}
