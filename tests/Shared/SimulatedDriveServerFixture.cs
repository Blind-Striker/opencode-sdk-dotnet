using System.Runtime.ExceptionServices;
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

    private readonly RealFileSystem _fileSystem = new();
    private CliWrapServerAdapter? _adapter;
    private DriveController? _controller;
    private string? _postReadinessDiagnostic;
    private string? _preReadinessDiagnostic;
    private TestRunRoot? _runRoot;
    private bool _retainLogs;

    public Uri Endpoint => Adapter.Endpoint;

    internal DriveController Controller =>
        _controller ?? throw new InvalidOperationException("The fixture has not initialized.");

    internal CliWrapServerAdapter Adapter =>
        _adapter ?? throw new InvalidOperationException("The fixture has not initialized.");

    internal TestRunRoot RunRoot =>
        _runRoot ?? throw new InvalidOperationException("The fixture has not initialized.");

    internal string PostReadinessDiagnostic =>
        _postReadinessDiagnostic ?? throw new InvalidOperationException("The fixture has not captured readiness diagnostics.");

    internal string PreReadinessDiagnostic =>
        _preReadinessDiagnostic ?? throw new InvalidOperationException("The fixture has not captured readiness diagnostics.");

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
        var persistentHost = new PersistentSimulationServerCommand(_fileSystem);
        var command = persistentHost.Resolve();
        using var gate = await DrivePortGate.AcquireAsync(_fileSystem, GateTimeout);
        var manifest = DriveManifest.Write(_fileSystem, registry);
        var environment = ServerIsolation.Environment(_fileSystem, runRoot.Path);
        environment["OPENCODE_SIMULATE"] = "1";
        environment["OPENCODE_DRIVE"] = manifest.InstanceName;
        environment["DRIVE_REGISTRY_DIR"] = registry;
        environment["OPENCODE_CONFIG_CONTENT"] = SimulationConfigSeed.Json;
        environment["OPENCODE_LOG_LEVEL"] = "INFO";
        environment["OPENCODE_PRINT_LOGS"] = "1";
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
                persistentHost.RepositoryRoot, "external", "opencode", "packages", "cli"),
            ReadinessTimeout,

            // Captured the instant the adapter object exists so a startup failure still has
            // stdout/stderr to write out on teardown; StartAsync disposes its own local on every
            // failure path but never returns it (PinnedOpenCodeServerFixture, same reason).
            onConstructed: created => _adapter = created);

        // Capture the two startup milestones while they are current. The adapter intentionally
        // retains only a bounded stderr tail, so a later test class must not depend on finding old
        // lines after other per-session consumers have produced more diagnostics.
        using (var diagnosticWindow = new CancellationTokenSource(ControllerTimeout))
        {
            _preReadinessDiagnostic = await _adapter.WaitForErrorLineAsync(
                "persistent simulation host starting",
                diagnosticWindow.Token);
            _postReadinessDiagnostic = await _adapter.WaitForErrorLineAsync(
                "persistent simulation host ready",
                diagnosticWindow.Token);
        }

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
        var failures = new List<Exception>();
        try
        {
            if (_controller is not null)
            {
                await _controller.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (_adapter is not null)
        {
            try
            {
                await _adapter.DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            CaptureDiagnosticFailure(_adapter, failures);

            if (keep && _runRoot is not null)
            {
                try
                {
                    await _adapter.WriteLogsAsync(_fileSystem, _fileSystem.Path.Combine(_runRoot.Path, "logs"));
                    Console.WriteLine($"Simulated server logs retained under: {_runRoot.Path}");
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        if (!keep)
        {
            _runRoot?.Dispose();
        }

        if (failures.Count is 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("Multiple simulated server teardown failures occurred.", failures);
        }
    }

    private void CaptureDiagnosticFailure(
        CliWrapServerAdapter adapter,
        List<Exception> failures)
    {
        var missing = new List<string>();
        if (_preReadinessDiagnostic is null)
        {
            missing.Add("persistent simulation host starting");
        }

        if (_postReadinessDiagnostic is null)
        {
            missing.Add("persistent simulation host ready");
        }

        if (!adapter.HasErrorLine("persistent simulation host stdin closed"))
        {
            missing.Add("persistent simulation host stdin closed");
        }

        if (missing.Count > 0)
        {
            failures.Add(new InvalidOperationException(
                "The persistent simulation host did not retain diagnostics: " + string.Join(", ", missing) + "."));
            return;
        }

        Console.WriteLine(
            "Persistent simulation host retained pre-readiness, post-readiness, and stdin-EOF diagnostics for process " +
            adapter.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
    }
}
