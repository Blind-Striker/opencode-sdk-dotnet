using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// The launch recipe of the persistent simulation host: its command, the CLI-package working
/// directory bun needs to resolve the monorepo workspace, the isolated environment with the
/// simulation switches and the per-run drive manifest, and that manifest. The caller holds
/// <see cref="DrivePortGate"/> from <see cref="Prepare"/> until the server is ready, because the
/// manifest names the ports the host binds later.
/// </summary>
internal sealed record SimulatedServerLaunch
{
    public required IReadOnlyList<string> Command { get; init; }

    public required string WorkingDirectory { get; init; }

    public required IReadOnlyDictionary<string, string> Environment { get; init; }

    public required DriveManifest Manifest { get; init; }

    /// <summary>
    /// Bounded well above the realistic worst case - every local target-framework leg starting a
    /// simulated server back to back, each within the readiness bound - so a genuinely wedged
    /// gate holder still fails loudly instead of hanging the suite.
    /// </summary>
    public static TimeSpan GateTimeout { get; } = TimeSpan.FromMinutes(15);

    /// <summary>The launcher options for this recipe under <see cref="OwnedServerPolicy"/>, retaining output in <paramref name="output"/>.</summary>
    public OpenCodeServerOptions Options(OpenCodeServerOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        return new OpenCodeServerOptions
        {
            Command = Command,
            WorkingDirectory = WorkingDirectory,
            Environment = Environment,
            ReadinessTimeout = OwnedServerPolicy.ReadinessTimeout,
            GracefulShutdownTimeout = OwnedServerPolicy.GracefulShutdownTimeout,
            Output = output,
        };
    }

    public static SimulatedServerLaunch Prepare(IFileSystem fileSystem, TestRunRoot runRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(runRoot);

        var registry = runRoot.CreateSubdirectory("drive");
        var persistentHost = new PersistentSimulationServerCommand(fileSystem);
        var manifest = DriveManifest.Write(fileSystem, registry);
        var environment = ServerIsolation.Environment(fileSystem, runRoot.Path);
        environment["OPENCODE_SIMULATE"] = "1";
        environment["OPENCODE_DRIVE"] = manifest.InstanceName;
        environment["DRIVE_REGISTRY_DIR"] = registry;
        environment["OPENCODE_CONFIG_CONTENT"] = SimulationConfigSeed.Json;
        environment["OPENCODE_LOG_LEVEL"] = "INFO";
        environment["OPENCODE_PRINT_LOGS"] = "1";
        return new SimulatedServerLaunch
        {
            Command = persistentHost.Resolve(),

            // Anchored at the pinned CLI package: bun resolves the monorepo's workspace and
            // tsconfig from the process working directory, not from the absolute entry-file
            // path, and a scratch directory outside the checkout fails the source run before
            // readiness. Every global root the host touches stays isolated through the
            // environment regardless of this directory.
            WorkingDirectory = fileSystem.Path.Combine(
                persistentHost.RepositoryRoot, "external", "opencode", "packages", "cli"),
            Environment = environment,
            Manifest = manifest,
        };
    }
}
