using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The pinned client's <c>ensure</c> (<c>promise/service.ts:33-116</c>): a wall-clock deadline
/// loop that reuses a healthy compatible service, replaces a version-mismatched one, and otherwise
/// spawns at most two detached contenders until a service becomes discoverable. Version policy is
/// applied outside the loop; the loop only sees an already-resolved predicate.
/// </summary>
internal sealed class ServiceEnsurer(
    IServiceEnvironment environment,
    IServiceFileSystem fileSystem,
    IServiceInfoProbe probe,
    IServiceContenderSpawner spawner,
    IServicePtyHandoff handoff,
    IServiceProcessControl processControl,
    IServiceClock clock,
    ExecutableResolver executableResolver,
    ServiceTiming timing)
{
    internal const string TimeoutMessage = "Timed out waiting for the background service to start.";
    internal const string FailedMessage = "The background service failed to start.";
    internal const string MismatchMessage = "The background server version does not match this client.";

    private static readonly ServiceProbeResult NoService = new(State: null, Version: null, TimedOut: false);

    /// <summary>Ensures a healthy compatible service is running and returns its registration.</summary>
    /// <param name="options">The caller's options; null means every default.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The ready service's registration. The public door builds a non-owning handle from it.</returns>
    /// <exception cref="ArgumentException">The options are blank or contradictory.</exception>
    /// <exception cref="OpenCodeServerException">No user home resolves, the loop timed out, the service failed, or a version mismatch was refused.</exception>
    public async Task<ServiceRegistration> EnsureAsync(
        OpenCodeServerEnsureOptions? options,
        CancellationToken cancellationToken)
    {
        var selection = ServiceSelection.Snapshot(options);
        cancellationToken.ThrowIfCancellationRequested();

        var paths = new ServicePathResolver(environment).Resolve(selection);
        await new ServiceMigration(fileSystem).ApplyAsync(selection, paths, cancellationToken).ConfigureAwait(false);

        var policy = new ServiceVersionPolicy(
            options?.VersionPolicy ?? OpenCodeServerVersionPolicy.Ignore,
            options?.ExpectedVersion);

        if (policy.RequiresPreamble)
        {
            var preamble = await RunPreambleAsync(options, policy, cancellationToken).ConfigureAwait(false);
            switch (preamble)
            {
                case ServiceVersionPreamble.ReturnExisting existing:
                    return existing.Registration;
                case ServiceVersionPreamble.ThrowMismatch:
                    throw new OpenCodeServerException(MismatchMessage);
            }
        }

        return await RunLoopAsync(options, paths, policy, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ServiceVersionPreamble> RunPreambleAsync(
        OpenCodeServerEnsureOptions? options,
        ServiceVersionPolicy policy,
        CancellationToken cancellationToken)
    {
        var discovery = new ServiceDiscovery(environment, fileSystem, probe);
        var compatible = await discovery
            .DiscoverAsync(ToDiscoverOptions(options, policy.ExpectedVersion), cancellationToken)
            .ConfigureAwait(false);
        var existing = compatible is not null
            ? compatible
            : await discovery
                .DiscoverAsync(ToDiscoverOptions(options, expectedVersion: null), cancellationToken)
                .ConfigureAwait(false);
        return ServiceVersionPolicy.DecideErrorPreamble(compatible, existing);
    }

    private async Task<ServiceRegistration> RunLoopAsync(
        OpenCodeServerEnsureOptions? options,
        ServicePaths paths,
        ServiceVersionPolicy policy,
        CancellationToken cancellationToken)
    {
        var command = SnapshotCommand(options);
        var state = new ElectionState { SpawnDelay = timing.SpawnDelay };
        var deadline = clock.UtcNow + timing.PromiseTimeout;
        var registrationFile = new ServiceRegistrationFile(fileSystem);
        var terminator = new ServiceTerminator(fileSystem, processControl, timing);

        try
        {
            while (true)
            {
                var won = await IterateOnceAsync(
                        options,
                        paths,
                        policy,
                        command,
                        state,
                        terminator,
                        registrationFile,
                        deadline,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (won is not null)
                {
                    return won;
                }
            }
        }
        finally
        {
            foreach (var contender in state.Contenders)
            {
                contender.Release();
            }
        }
    }

    private async Task<ServiceRegistration?> IterateOnceAsync(
        OpenCodeServerEnsureOptions? options,
        ServicePaths paths,
        ServiceVersionPolicy policy,
        string[] command,
        ElectionState state,
        ServiceTerminator terminator,
        ServiceRegistrationFile registrationFile,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = clock.UtcNow;
        if (now >= deadline)
        {
            throw new OpenCodeServerException(TimeoutMessage);
        }

        var registration = await registrationFile
            .TryReadAsync(paths.RegistrationFile, cancellationToken)
            .ConfigureAwait(false);
        var answer = registration is null
            ? NoService
            : await probe.ProbeAsync(registration, cancellationToken).ConfigureAwait(false);

        var decision = new ServiceElectionIteration(
            now,
            deadline,
            registration,
            answer,
            state.Timeouts,
            ObserveAll(state.Contenders),
            state.LastSpawn,
            state.SpawnDelay,
            timing,
            policy.Matches(answer.Version)).Decide();

        return await ApplyDecisionAsync(
                decision,
                options,
                paths,
                command,
                state,
                terminator,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ServiceRegistration?> ApplyDecisionAsync(
        ServiceElectionDecision decision,
        OpenCodeServerEnsureOptions? options,
        ServicePaths paths,
        string[] command,
        ElectionState state,
        ServiceTerminator terminator,
        CancellationToken cancellationToken)
    {
        switch (decision)
        {
            case ServiceElectionDecision.ReturnReady ready:
                await handoff.CompleteAsync(paths.RegistrationFile, ready.Registration, cancellationToken)
                    .ConfigureAwait(false);
                return ready.Registration;
            case ServiceElectionDecision.ThrowTimeout:
                throw new OpenCodeServerException(TimeoutMessage);
            case ServiceElectionDecision.ThrowFailed:
                throw new OpenCodeServerException(FailedMessage);
            case ServiceElectionDecision.ThrowContenderFailure failure:
                Harvest(state.Contenders, failure.HarvestedIndices);
                throw failure.Failure;
            case ServiceElectionDecision.Continue next:
                Harvest(state.Contenders, next.HarvestedIndices);
                await ApplyEffectsAsync(
                        next.Effects,
                        paths,
                        command,
                        options?.Environment,
                        state,
                        options?.OnStart,
                        terminator,
                        cancellationToken)
                    .ConfigureAwait(false);
                state.Timeouts = next.Timeouts;
                state.LastSpawn = next.LastSpawn;
                state.SpawnDelay = next.SpawnDelay;
                await Task.Delay(timing.PollInterval, cancellationToken).ConfigureAwait(false);
                return null;
            default:
                throw new OpenCodeServerException("The background-service election produced an unknown decision.");
        }
    }

    private async Task ApplyEffectsAsync(
        IReadOnlyList<ServiceElectionEffect> effects,
        ServicePaths paths,
        string[] command,
        IReadOnlyDictionary<string, string>? callerEnvironment,
        ElectionState state,
        Action<OpenCodeServerEnsureReason, string?>? onStart,
        ServiceTerminator terminator,
        CancellationToken cancellationToken)
    {
        foreach (var effect in effects)
        {
            await ApplyEffectAsync(
                    effect,
                    paths,
                    command,
                    callerEnvironment,
                    state,
                    onStart,
                    terminator,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ApplyEffectAsync(
        ServiceElectionEffect effect,
        ServicePaths paths,
        string[] command,
        IReadOnlyDictionary<string, string>? callerEnvironment,
        ElectionState state,
        Action<OpenCodeServerEnsureReason, string?>? onStart,
        ServiceTerminator terminator,
        CancellationToken cancellationToken)
    {
        switch (effect)
        {
            case ServiceElectionEffect.AnnounceMissing:
                Announce(onStart, state, OpenCodeServerEnsureReason.Missing, previousVersion: null);
                break;
            case ServiceElectionEffect.AnnounceVersionMismatch(var previousVersion):
                Announce(onStart, state, OpenCodeServerEnsureReason.VersionMismatch, previousVersion);
                break;
            case ServiceElectionEffect.ClearHandoff:
                await handoff.ClearAsync(paths.RegistrationFile, cancellationToken).ConfigureAwait(false);
                break;
            case ServiceElectionEffect.Terminate(var target):
                await terminator.TerminateAsync(target, paths.RegistrationFile, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ServiceElectionEffect.ReplaceIncompatible(var target, var prepareHandoff):
                _ = await TryReplaceIncompatibleAsync(paths.RegistrationFile, target, prepareHandoff, terminator, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ServiceElectionEffect.Spawn:
                await SpawnContenderAsync(paths, command, callerEnvironment, state.Contenders, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private async Task<bool> TryReplaceIncompatibleAsync(
        string registrationFile,
        ServiceRegistration registration,
        bool prepareHandoff,
        ServiceTerminator terminator,
        CancellationToken cancellationToken)
    {
        try
        {
            if (prepareHandoff)
            {
                await handoff.PrepareAsync(registrationFile, registration, timing.RequestTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await handoff.ClearAsync(registrationFile, cancellationToken).ConfigureAwait(false);
            }

            await terminator.TerminateAsync(registration, registrationFile, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OpenCodeServerException)
        {
            // The pinned client's stop(...).catch(() => undefined): replacement continues.
            return false;
        }
    }

    private async Task SpawnContenderAsync(
        ServicePaths paths,
        string[] command,
        IReadOnlyDictionary<string, string>? callerEnvironment,
        List<ServiceContender> contenders,
        CancellationToken cancellationToken)
    {
        try
        {
            var overlay = await OverlayEnvironmentAsync(paths, callerEnvironment, cancellationToken)
                .ConfigureAwait(false);
            var withHandoff = await handoff
                .EnvironmentAsync(paths.RegistrationFile, overlay, cancellationToken)
                .ConfigureAwait(false);
            var executable = executableResolver.Resolve(command[0]);
            var arguments = command.Length == 1 ? [] : command.Skip(1).ToArray();
            var startInfo = ServiceContenderStartComposer.Compose(executable, arguments, withHandoff);
            contenders.Add(spawner.Spawn(startInfo));
        }
        catch (OpenCodeServerException)
        {
            throw;
        }
        catch (Exception cause)
        {
            throw new OpenCodeServerException("Failed to start the background service.", cause);
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> OverlayEnvironmentAsync(
        ServicePaths paths,
        IReadOnlyDictionary<string, string>? callerEnvironment,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string>? overlay = null;
        if (paths.ConfigFile is { } configFile)
        {
            var bytes = await new ServiceRegistrationFile(fileSystem)
                .TryReadBytesAsync(configFile, cancellationToken)
                .ConfigureAwait(false);
            if (bytes is not null && ServiceConfigReader.TryReadEnvironment(bytes) is { } configEnvironment)
            {
                overlay = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in configEnvironment)
                {
                    overlay[entry.Key] = entry.Value;
                }
            }
        }

        if (callerEnvironment is null)
        {
            return overlay;
        }

        overlay ??= new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in callerEnvironment)
        {
            overlay[entry.Key] = entry.Value;
        }

        return overlay;
    }

    private static void Announce(
        Action<OpenCodeServerEnsureReason, string?>? onStart,
        ElectionState state,
        OpenCodeServerEnsureReason reason,
        string? previousVersion)
    {
        if (state.Announced)
        {
            return;
        }

        state.Announced = true;
        onStart?.Invoke(reason, previousVersion);
    }

    private static void Harvest(List<ServiceContender> contenders, IReadOnlyList<int> harvestedIndices)
    {
        for (var index = harvestedIndices.Count - 1; index >= 0; index--)
        {
            contenders.RemoveAt(harvestedIndices[index]);
        }
    }

    private static ServiceContenderObservation[] ObserveAll(List<ServiceContender> contenders)
    {
        if (contenders.Count == 0)
        {
            return [];
        }

        var observations = new ServiceContenderObservation[contenders.Count];
        for (var index = 0; index < contenders.Count; index++)
        {
            var contender = contenders[index];
            observations[index] = new ServiceContenderObservation(
                contender.IsFinished,
                contender.ExitCode == 0,
                contender.TryGetFailure());
        }

        return observations;
    }

    private static OpenCodeServerDiscoverOptions ToDiscoverOptions(
        OpenCodeServerEnsureOptions? options,
        string? expectedVersion) =>
        new()
        {
            Channel = options?.Channel,
            RegistrationFilePath = options?.RegistrationFilePath,
            InstalledVersion = options?.InstalledVersion,
            ExpectedVersion = expectedVersion,
        };

    private sealed class ElectionState
    {
        public bool Announced { get; set; }

        public ServiceTimeoutCounter? Timeouts { get; set; }

        public DateTimeOffset? LastSpawn { get; set; }

        public TimeSpan SpawnDelay { get; set; }

        public List<ServiceContender> Contenders { get; } = [];
    }

    private static string[] SnapshotCommand(OpenCodeServerEnsureOptions? options)
    {
        if (options is null)
        {
            return ["opencode", "serve", "--service"];
        }

        var command = options.Command;
        if (command is not { Count: > 0 })
        {
            throw new ArgumentException(
                "OpenCodeServerEnsureOptions.Command needs the executable and its leading arguments.",
                nameof(options));
        }

        var snapshot = new string[command.Count];
        for (var index = 0; index < command.Count; index++)
        {
            var entry = command[index];
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw new ArgumentException("OpenCodeServerEnsureOptions.Command entries cannot be blank.", nameof(options));
            }

            snapshot[index] = entry;
        }

        return snapshot;
    }
}
