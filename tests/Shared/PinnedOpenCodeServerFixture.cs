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
    private CliWrapServerAdapter? _adapter;
    private TestRunRoot? _runRoot;
    private bool _retainLogs;

    public Uri Endpoint => Adapter.Endpoint;

    internal CliWrapServerAdapter Adapter =>
        _adapter ?? throw new InvalidOperationException("The fixture has not initialized.");

    internal TestRunRoot RunRoot =>
        _runRoot ?? throw new InvalidOperationException("The fixture has not initialized.");

    public async Task InitializeAsync()
    {
        _runRoot = new TestRunRoot(_fileSystem);
        var pinnedCommand = new PinnedServerCommand(_fileSystem);
        try
        {
            _adapter = await CliWrapServerAdapter.StartAsync(
                pinnedCommand.Resolve(),
                BuildEnvironment(_runRoot.Path),
                // Bun's workspace/tsconfig discovery for the pinned monorepo's JSX packages
                // walks from the process's working directory, not from the absolute entry-file
                // path (Task 2's confirmed repro, OpenCodeServerLifecycleTests.StartPinnedAsync):
                // a scratch directory outside the checkout leaves that discovery unable to find
                // the workspace root, and the source-run server fails before readiness with
                // "Cannot find module 'react/jsx-dev-runtime'". Anchoring at the CLI package is
                // what upstream's own "dev" script does; state/data/cache/config stay isolated
                // through the environment below regardless of this directory.
                _fileSystem.Path.Combine(
                    pinnedCommand.RepositoryRoot, "external", "opencode", "packages", "cli"),
                ReadinessTimeout);
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
