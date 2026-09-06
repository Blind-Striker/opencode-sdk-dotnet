using System.Runtime.ExceptionServices;
using OpenCode.Sdk.TestSupport.Ownership;
using Testably.Abstractions;
using TUnit.Core.Interfaces;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// The exact-pin server fixture in simulation mode (design §7.4): a named drive instance with a
/// per-run manifest (never the fixed default ports), a config-seeded simulated provider, and an
/// attached drive controller. Simulation denies all unregistered outbound network by
/// construction (backend/index.ts:29-35), so the workflow runs with no provider credentials.
/// </summary>
public sealed class SimulatedDriveServerFixture : IAsyncInitializer, IAsyncDisposable, ITestEndEventReceiver
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
    private ServerFailureArtifacts? _artifacts;
    private int _disposed;
    public Uri Endpoint => Adapter.Endpoint;

    public int Order => 0;

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
        catch (Exception exception)
        {
            _retainLogs = true;
            MarkFailure(exception, "fixture initialization", "phase=server startup");
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

    private ServerFailureArtifacts Artifacts => _artifacts ??= new ServerFailureArtifacts(
        _fileSystem, new TestResultsDirectory(_fileSystem).Resolve(Environment.GetCommandLineArgs()));

    private void MarkFailure(Exception exception, string test, string details)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _ = Artifacts.Mark(exception, test, details, TestContext.Current?.Id);
    }

    public ValueTask OnTestEnd(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Execution.Result?.Exception is { } failure)
        {
            var path = Artifacts.Mark(
                failure,
                context.Metadata.TestDetails.ClassType.FullName + "." + context.Metadata.TestName,
                "phase=test-body; receiver observes the body result, before later teardown outcomes",
                context.Id);
            if (Artifacts.Report(failure, context.Id))
            {
                context.Output.AttachArtifact(path, "Owned simulated server failure diagnostics");
            }
        }

        return default;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        var keep = ShouldRetainLogs;
        var recorded = _artifacts?.Failures;
        var primary = recorded is { Count: > 0 } ? recorded[0].Exception : null;
        var teardown = new OwnedCleanup(TimeSpan.FromSeconds(25));
        if (_controller is not null)
        {
            teardown.Own("simulation controller teardown", async _ => await _controller.DisposeAsync());
        }

        if (_adapter is not null)
        {
            teardown.Own("simulated server teardown", async _ => await _adapter.DisposeAsync());
            teardown.Own("simulated server diagnostic contract", _ =>
            {
                EnsureDiagnosticContract(_adapter);
                return Task.CompletedTask;
            });
        }

        Exception? failure = null;
        try
        {
            await teardown.CompleteAsync(primary);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is not null || keep)
        {
            if (failure is not null && !Artifacts.Failures.Any(item => ReferenceEquals(item.Exception, failure)))
            {
                _ = Artifacts.Mark(failure, "fixture disposal", "phase=owned-server-teardown");
            }

            var capture = new ServerFailureCapture(
                Artifacts, _fileSystem, new OwnedOperationDeadline());
            failure = await capture.CaptureAsync(_adapter, external: false, failure, teardown.OperationFailures);
            Console.WriteLine("Simulated server diagnostics: " + Artifacts.Directory);
        }

        if (failure is null && !keep)
        {
            _runRoot?.Dispose();
        }

        if (failure is not null && !(_artifacts?.IsReported(failure) ?? false))
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private bool ShouldRetainLogs => _retainLogs || _artifacts?.Failures.Count > 0 || string.Equals(
        Environment.GetEnvironmentVariable("OPENCODE_SDK_TESTS_KEEP_LOGS"), "1", StringComparison.Ordinal);

    private void EnsureDiagnosticContract(CliWrapServerAdapter adapter)
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
            throw new InvalidOperationException(
                "The persistent simulation host did not retain diagnostics: " + string.Join(", ", missing) + ".");
        }

        Console.WriteLine(
            "Persistent simulation host retained pre-readiness, post-readiness, and stdin-EOF diagnostics for process " +
            adapter.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
    }
}
