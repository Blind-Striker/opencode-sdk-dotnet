using System.Text;
using NSubstitute;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Internal.BackgroundService;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions.Testing;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// The Ensure election loop over substituted seams: no live process, no real registration, no
/// socket. Announce-once, contender release, version policy, and the ready/failed doors.
/// </summary>
public sealed class ServiceEnsurerTests
{
    private const string Version = "2.0.3";
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly ServiceTiming Timing = new(
        RequestTimeout: TimeSpan.FromSeconds(2),
        PollInterval: TimeSpan.Zero,
        SpawnDelay: TimeSpan.Zero,
        MaxSpawnDelay: TimeSpan.FromSeconds(30),
        PromiseTimeout: TimeSpan.FromSeconds(120),
        StopPollInterval: TimeSpan.FromMilliseconds(2),
        StopPollAttempts: 3);

    private static readonly ServiceProbeResult Ready =
        new(ServiceState.Ready, Version, TimedOut: false);

    private static readonly ServiceProbeResult Failed =
        new(ServiceState.Failed, Version, TimedOut: false);

    private readonly MockFileSystem _fileSystem = new();
    private readonly IServiceEnvironment _environment = Substitute.For<IServiceEnvironment>();
    private readonly IServiceInfoProbe _probe = Substitute.For<IServiceInfoProbe>();
    private readonly IServiceContenderSpawner _spawner = Substitute.For<IServiceContenderSpawner>();
    private readonly IServicePtyHandoff _handoff = Substitute.For<IServicePtyHandoff>();
    private readonly IServiceProcessControl _processControl = Substitute.For<IServiceProcessControl>();
    private readonly IServiceClock _clock = Substitute.For<IServiceClock>();
    private readonly List<IServiceContender> _spawned = [];
    private int _clockReads;

    public ServiceEnsurerTests()
    {
        _environment.GetEnvironmentVariable("XDG_STATE_HOME").Returns(Root("state"));
        _environment.GetEnvironmentVariable("XDG_CONFIG_HOME").Returns(Root("config"));
        _clock.UtcNow.Returns(_ => Start);
        _handoff.EnvironmentAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var caller = call.Arg<IReadOnlyDictionary<string, string>?>();
                var overlay = new Dictionary<string, string?>(StringComparer.Ordinal);
                if (caller is not null)
                {
                    foreach (var entry in caller)
                    {
                        overlay[entry.Key] = entry.Value;
                    }
                }

                return overlay;
            });
        _handoff.CompleteAsync(Arg.Any<string>(), Arg.Any<ServiceRegistration>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _handoff.ClearAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _handoff.PrepareAsync(Arg.Any<string>(), Arg.Any<ServiceRegistration>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _spawner.Spawn(Arg.Any<IServiceContenderSpawner.ContenderStartInfo>()).Returns(_ => HoldContender());
    }

    [After(Test)]
    public void DisposeHeldContenders()
    {
        foreach (var contender in _spawned)
        {
            contender.Dispose();
        }
    }

    [Test]
    public async Task EnsureAsync_Should_Return_A_Ready_Compatible_Service_Without_Spawning()
    {
        SeedModern();
        Answer(Ready);

        var registration = await Ensurer().EnsureAsync(options: null, CancellationToken.None);

        await Assert.That(registration.Password).IsEqualTo(ServiceRegistrationData.Password);
        await Assert.That(registration.ProcessId).IsEqualTo(48213);
        _ = _spawner.DidNotReceiveWithAnyArgs().Spawn(default!);
        _ = _handoff.Received(1).CompleteAsync(Arg.Any<string>(), Arg.Any<ServiceRegistration>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EnsureAsync_Should_Throw_When_A_Compatible_Service_Failed()
    {
        SeedModern();
        Answer(Failed);

        var exception = await Assert
            .That(async () => _ = await Ensurer().EnsureAsync(options: null, CancellationToken.None))
            .Throws<OpenCodeServerException>();

        await Assert.That(exception!.Message).IsEqualTo(ServiceEnsurer.FailedMessage);
        _ = _spawner.DidNotReceiveWithAnyArgs().Spawn(default!);
    }

    [Test]
    public async Task EnsureAsync_Should_Ignore_ExpectedVersion_When_The_Policy_Is_Ignore()
    {
        SeedModern();
        Answer(Ready);

        var registration = await Ensurer().EnsureAsync(
            new OpenCodeServerEnsureOptions
            {
                ExpectedVersion = "2.0.2",
                VersionPolicy = OpenCodeServerVersionPolicy.Ignore,
            },
            CancellationToken.None);

        await Assert.That(registration.ProcessId).IsEqualTo(48213);
        _ = _spawner.DidNotReceiveWithAnyArgs().Spawn(default!);
    }

    [Test]
    public async Task EnsureAsync_Should_Throw_On_Mismatch_Without_Entering_The_Loop_When_The_Policy_Is_Error()
    {
        SeedModern();
        Answer(Ready);

        var exception = await Assert
            .That(async () => _ = await Ensurer().EnsureAsync(
                new OpenCodeServerEnsureOptions
                {
                    ExpectedVersion = "2.0.2",
                    VersionPolicy = OpenCodeServerVersionPolicy.Error,
                },
                CancellationToken.None))
            .Throws<OpenCodeServerException>();

        await Assert.That(exception!.Message).IsEqualTo(ServiceEnsurer.MismatchMessage);
        _ = _spawner.DidNotReceiveWithAnyArgs().Spawn(default!);
        _ = _handoff.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default);
    }

    [Test]
    public async Task EnsureAsync_Should_Return_A_Matching_Service_Without_Entering_The_Loop_When_The_Policy_Is_Error()
    {
        SeedModern();
        Answer(Ready);

        var registration = await Ensurer().EnsureAsync(
            new OpenCodeServerEnsureOptions
            {
                ExpectedVersion = Version,
                VersionPolicy = OpenCodeServerVersionPolicy.Error,
            },
            CancellationToken.None);

        await Assert.That(registration.ProcessId).IsEqualTo(48213);
        _ = _spawner.DidNotReceiveWithAnyArgs().Spawn(default!);
    }

    [Test]
    public async Task EnsureAsync_Should_Announce_Missing_At_Most_Once()
    {
        var reasons = new List<OpenCodeServerEnsureReason>();
        JumpClockAfter(iterations: 3);

        var exception = await Assert
            .That(async () => _ = await Ensurer().EnsureAsync(
                new OpenCodeServerEnsureOptions { OnStart = (reason, _) => reasons.Add(reason) },
                CancellationToken.None))
            .Throws<OpenCodeServerException>();

        await Assert.That(exception!.Message).IsEqualTo(ServiceEnsurer.TimeoutMessage);
        await Assert.That(reasons.Count).IsEqualTo(1);
        await Assert.That(reasons[0]).IsEqualTo(OpenCodeServerEnsureReason.Missing);
        await Assert.That(_spawned.Count).IsEqualTo(2);
    }

    [Test]
    public async Task EnsureAsync_Should_Release_Contenders_When_The_Loop_Times_Out()
    {
        JumpClockAfter(iterations: 3);

        _ = await Assert
            .That(async () => _ = await Ensurer().EnsureAsync(options: null, CancellationToken.None))
            .Throws<OpenCodeServerException>();

        await Assert.That(_spawned.Count).IsGreaterThan(0);
        foreach (var contender in _spawned)
        {
            contender.Received(1).Release();
        }
    }

    [Test]
    public async Task EnsureAsync_Should_Refuse_Contradictory_Options_Before_Touching_Anything()
    {
        var options = new OpenCodeServerEnsureOptions
        {
            Channel = "dev",
            RegistrationFilePath = Path("elsewhere", "registration.json"),
        };

        var exception = await Assert
            .That(async () => _ = await Ensurer().EnsureAsync(options, CancellationToken.None))
            .Throws<ArgumentException>();

        await Assert.That(exception!.ParamName).IsEqualTo("options");
        _ = _environment.DidNotReceiveWithAnyArgs().GetEnvironmentVariable(default!);
    }

    [Test]
    public async Task EnsureAsync_Should_Rethrow_Caller_Cancellation()
    {
        var cancelled = new CancellationToken(canceled: true);

        _ = await Assert
            .That(async () => _ = await Ensurer().EnsureAsync(options: null, cancelled))
            .Throws<OperationCanceledException>();
    }

    private ServiceEnsurer Ensurer() =>
        new(
            _environment,
            new TestablyServiceFileSystem(_fileSystem),
            _probe,
            _spawner,
            _handoff,
            _processControl,
            _clock,
            new ExecutableResolver(new ExecutableSearchEnvironment
            {
                IsWindows = false,
                SearchPath = "/bin",
                SearchExtensions = null,
                CurrentDirectory = "/",
                FileExists = static _ => true,
            }),
            Timing);

    private void Answer(ServiceProbeResult result) =>
        _probe.ProbeAsync(Arg.Any<ServiceRegistration>(), Arg.Any<CancellationToken>()).Returns(result);

    private void JumpClockAfter(int iterations)
    {
        _clock.UtcNow.Returns(_ =>
        {
            _clockReads++;
            return _clockReads > iterations ? Start + Timing.PromiseTimeout : Start;
        });
    }

    /// <summary>A contender that stays live: never finished, no failure, no exit.</summary>
    private IServiceContender HoldContender()
    {
        var contender = Substitute.For<IServiceContender>();
        contender.ProcessId.Returns(9000 + _spawned.Count);
        _spawned.Add(contender);
        return contender;
    }

    private void SeedModern() =>
        Seed(SharedRegistrationPath(), new FixtureLoader().LoadJson("BackgroundService.registration-modern.json"));

    private void Seed(string path, string content)
    {
        _ = _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(path)!);
#pragma warning disable MA0045 // MockFileSystem has no async write on every TFM; same Seed as ServiceDiscoveryTests.
        _fileSystem.File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
#pragma warning restore MA0045
    }

    private string SharedRegistrationPath() => _fileSystem.Path.Combine(Root("state"), "opencode", "service.json");

    private string Root(string name) => Path(name);

    private string Path(params string[] segments) =>
        _fileSystem.Path.Combine([_fileSystem.Path.GetTempPath(), "service-ensure", .. segments]);
}
