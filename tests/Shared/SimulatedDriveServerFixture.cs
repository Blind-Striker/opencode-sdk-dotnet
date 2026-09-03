using Testably.Abstractions;
using TUnit.Core.Interfaces;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// The exact-pin server fixture in simulation mode (design §7.4): a named drive instance with a
/// per-run manifest (never the fixed default ports), a config-seeded simulated provider, and an
/// attached drive controller. Simulation denies all unregistered outbound network by
/// construction (backend/index.ts:29-35), so the workflow runs with no provider credentials.
/// </summary>
public sealed class SimulatedDriveServerFixture : IAsyncInitializer, IAsyncDisposable
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan ControllerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Bounded well above the realistic worst case - every local target-framework leg starting a
    /// simulated server back to back, each within <see cref="ReadinessTimeout"/> - so a genuinely
    /// wedged holder still fails loudly instead of hanging the suite.
    /// </summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Schema: config.ts:106 ('providers'), config/provider.ts:59-65; the builtin package
    /// resolves with no npm install (core provider.ts:69) and pins the chat route to the exact
    /// URL the simulation network claims (openai-compatible.ts settings.baseURL +
    /// openai-compatible-chat.ts:21 '/chat/completions' == openai-chat.ts:35-36
    /// DEFAULT_BASE_URL + PATH, the pair backend/openai.ts:105 matches on).
    /// </summary>
    private const string SimulationConfig =
        """{"providers":{"sim":{"name":"Simulated","package":"@opencode-ai/ai/providers/openai-compatible","settings":{"baseURL":"https://api.openai.com/v1","apiKey":"drive-lease"},"models":{"sim-model":{"name":"Simulated Model"}}}}}""";

    private readonly RealFileSystem _fileSystem = new();
    private CliWrapServerAdapter? _adapter;
    private DriveController? _controller;
    private TestRunRoot? _runRoot;
    private bool _retainLogs;

    public Uri Endpoint => Adapter.Endpoint;

    internal DriveController Controller =>
        _controller ?? throw new InvalidOperationException("The fixture has not initialized.");

    internal CliWrapServerAdapter Adapter =>
        _adapter ?? throw new InvalidOperationException("The fixture has not initialized.");

    internal TestRunRoot RunRoot =>
        _runRoot ?? throw new InvalidOperationException("The fixture has not initialized.");

    public async Task InitializeAsync()
    {
        _runRoot = new TestRunRoot(_fileSystem);
        try
        {
            _controller = await StartAsync(_runRoot);
            await _controller.HandshakeAsync();
            await _controller.AttachAsync();
        }
        catch
        {
            _retainLogs = true;
            throw;
        }
    }

    /// <summary>
    /// Brings up the simulated server and its attached-ready control socket. The whole
    /// reserve-manifest-through-bound-socket span runs under <see cref="DrivePortGate"/>: the
    /// manifest must name explicit ports the server binds later (manifest.ts:12-21), so without
    /// the gate two concurrently starting test hosts can be handed the same loopback port and
    /// the loser dies before readiness.
    /// </summary>
    private async Task<DriveController> StartAsync(TestRunRoot runRoot)
    {
        var registry = runRoot.CreateSubdirectory("drive");
        var pinnedCommand = new PinnedServerCommand(_fileSystem);
        var command = pinnedCommand.Resolve();
        using var gate = await DrivePortGate.AcquireAsync(_fileSystem, GateTimeout);
        var manifest = DriveManifest.Write(_fileSystem, registry);
        var environment = ServerIsolation.Environment(_fileSystem, runRoot.Path);
        environment["OPENCODE_SIMULATE"] = "1";
        environment["OPENCODE_DRIVE"] = manifest.InstanceName;
        environment["DRIVE_REGISTRY_DIR"] = registry;
        environment["OPENCODE_CONFIG_CONTENT"] = SimulationConfig;
        _adapter = await CliWrapServerAdapter.StartAsync(
            command,
            environment,

            // Anchored at the pinned CLI package for the same reason
            // PinnedOpenCodeServerFixture is: bun resolves the monorepo's workspace and tsconfig
            // from the process working directory, not from the absolute entry-file path, and a
            // scratch directory outside the checkout fails the source run before readiness. Every
            // global root the server touches stays isolated through the environment above
            // regardless of this directory.
            _fileSystem.Path.Combine(
                pinnedCommand.RepositoryRoot, "external", "opencode", "packages", "cli"),
            ReadinessTimeout,

            // Captured the instant the adapter object exists so a startup failure still has
            // stdout/stderr to write out on teardown; StartAsync disposes its own local on every
            // failure path but never returns it (PinnedOpenCodeServerFixture, same reason).
            onConstructed: created => _adapter = created);

        // The backend control socket is already listening when the readiness line is printed -
        // simulation builds the network layer eagerly at server start (backend/index.ts,
        // simulated-provider.ts:274-286) - so this connects once and fails the whole fixture
        // loudly if it cannot, rather than retrying blind. A successful connect is also what
        // proves the reserved port is now bound, which is what lets the gate above be released.
        return await DriveController.ConnectAsync(manifest.BackendEndpoint, ControllerTimeout);
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
        // The controller's teardown is the one that can misbehave - it awaits a receive loop it
        // does not fully own - and ending the child process is the step that must never be
        // skipped, so the adapter and the run root are torn down in a finally rather than after.
        try
        {
            if (_controller is not null)
            {
                await _controller.DisposeAsync();
            }
        }
        finally
        {
            if (_adapter is not null)
            {
                if (keep && _runRoot is not null)
                {
                    _adapter.WriteLogs(_fileSystem, _fileSystem.Path.Combine(_runRoot.Path, "logs"));
                    Console.WriteLine($"Simulated server logs retained under: {_runRoot.Path}");
                }

                await _adapter.DisposeAsync();
            }

            if (!keep)
            {
                _runRoot?.Dispose();
            }
        }
    }
}
