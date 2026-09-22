using System.Net;
using System.Text;
using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenCode.Sdk.Internal.BackgroundService;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions.Testing;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// The pinned client's sidecar orchestration (<c>pty-handoff.ts:13-97</c>) over the Testably
/// filesystem double and a real loopback HTTP daemon double: publication through a temporary plus
/// rename, the skip-when-current fast path, the 404 shutdown fallback, invalid and expired throws,
/// the concurrent-prepare recheck, the environment adoption matrix, completion by source, and the
/// idempotent clear.
/// </summary>
public sealed class ServicePtyHandoffTests
{
    private const int RegisteredPid = 48213;
    private const string SourceId = "srv_1";
    private const string Version = "2.0.3";
    private const string Password = "s3cr3t-p455w0rd";
    private const string FixedUrl = "http://127.0.0.1:49374";
    private const string HandoffPath = "/api/experimental/persistent-pty/handoff";
    private const string ShutdownPath = "/api/experimental/persistent-pty/shutdown";
    private const long TicketExpiry = 1700000060000;
    private const long NowMilliseconds = 1_700_000_000_000;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(NowMilliseconds);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly FixtureLoader Fixtures = new();
    private static readonly JsonElement Ticket = LoadTicket();

    private readonly MockFileSystem _fileSystem = new();
    private readonly IServiceClock _clock = Substitute.For<IServiceClock>();

    public ServicePtyHandoffTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    [Test]
    public async Task PrepareAsync_Should_Publish_A_Sidecar_From_The_Handoff_Ticket()
    {
        EnsureSidecarDirectory();
        await using var server = LoopbackHttpServer.Start(static _ =>
            Json(HttpStatusCode.OK, Fixtures.LoadJson("BackgroundService.pty-handoff-response-ticket.json")));
        var registration = Registration(server.Endpoint);

        await Handoff().PrepareAsync(RegistrationPath(), registration, RequestTimeout, CancellationToken.None);

        var sidecar = ReadSidecar();
        await Assert.That(sidecar).IsNotNull();
        await Assert.That(sidecar!.SourceId).IsEqualTo(SourceId);
        await Assert.That(sidecar.SourcePid).IsEqualTo(RegisteredPid);
        await Assert.That(sidecar.SourceUrl).IsEqualTo(registration.Url);
        await Assert.That(sidecar.ExpiresAt).IsEqualTo(TicketExpiry);
        await Assert.That(sidecar.Handoff!.Value.GetRawText()).IsEqualTo(Ticket.GetRawText());
        await Assert.That(TemporaryFiles()).IsEmpty();
        await Assert.That(server.RequestPaths).IsEquivalentTo([HandoffPath]);
    }

    [Test]
    public async Task PrepareAsync_Should_Reuse_A_Fresh_Matching_Sidecar_Without_Contacting_The_Daemon()
    {
        await using var server = LoopbackHttpServer.Start(static _ =>
            Json(HttpStatusCode.InternalServerError, "{}"));
        var registration = Registration(server.Endpoint);
        Seed(SidecarPath(), Sidecar(registration, handoff: null, NowMilliseconds + 60_000).ToUtf8Json());

        await Handoff().PrepareAsync(RegistrationPath(), registration, RequestTimeout, CancellationToken.None);

        await Assert.That(server.Requests).IsEmpty();
        await Assert.That(ReadSidecar()).IsNotNull();
    }

    [Test]
    public async Task PrepareAsync_Should_Shut_The_Daemon_Down_And_Publish_A_Null_Sidecar_When_The_Route_Is_Absent()
    {
        EnsureSidecarDirectory();
        await using var server = LoopbackHttpServer.Start(path => path == ShutdownPath
            ? NoContent()
            : Json(HttpStatusCode.NotFound, "{}"));
        var registration = Registration(server.Endpoint);

        await Handoff().PrepareAsync(RegistrationPath(), registration, RequestTimeout, CancellationToken.None);

        var sidecar = ReadSidecar();
        await Assert.That(sidecar).IsNotNull();
        await Assert.That(sidecar!.Handoff).IsNull();
        await Assert.That(sidecar.ExpiresAt).IsEqualTo(NowMilliseconds + 30_000d);
        await Assert.That(server.RequestPaths).Contains(ShutdownPath);
    }

    [Test]
    public async Task PrepareAsync_Should_Throw_When_The_Handoff_Is_Already_Expired()
    {
        await using var server = LoopbackHttpServer.Start(static _ =>
            Json(HttpStatusCode.OK, Fixtures.LoadJson("BackgroundService.pty-handoff-response-expired.json")));

        var exception = await Assert
            .That(async () => await PrepareAsync(server.Endpoint))
            .Throws<OpenCodeServerException>();

        await Assert.That(exception!.Message).IsEqualTo(ServicePtyHandoff.InvalidHandoffMessage);
        await Assert.That(ReadSidecar()).IsNull();
    }

    [Test]
    [Arguments("BackgroundService.pty-handoff-response-invalid.json")]
    [Arguments("BackgroundService.pty-handoff-response-missing.json")]
    public async Task PrepareAsync_Should_Throw_When_The_Handoff_Body_Is_Invalid(string fixture)
    {
        await using var server = LoopbackHttpServer.Start(_ =>
            Json(HttpStatusCode.OK, Fixtures.LoadJson(fixture)));

        var exception = await Assert
            .That(async () => await PrepareAsync(server.Endpoint))
            .Throws<OpenCodeServerException>();

        await Assert.That(exception!.Message).IsEqualTo(ServicePtyHandoff.PrepareFailedMessage);
        await Assert.That(ReadSidecar()).IsNull();
    }

    [Test]
    public async Task PrepareAsync_Should_Throw_When_The_Daemon_Answers_An_Error_And_No_Concurrent_Sidecar_Exists()
    {
        await using var server = LoopbackHttpServer.Start(static _ =>
            Json(HttpStatusCode.InternalServerError, "{}"));

        var exception = await Assert
            .That(async () => await PrepareAsync(server.Endpoint))
            .Throws<OpenCodeServerException>();

        await Assert.That(exception!.Message).IsEqualTo(ServicePtyHandoff.PrepareFailedMessage);
        await Assert.That(ReadSidecar()).IsNull();
    }

    [Test]
    public async Task PrepareAsync_Should_Report_A_Publish_Failure_As_A_Server_Failure()
    {
        await using var server = LoopbackHttpServer.Start(static _ =>
            Json(HttpStatusCode.OK, Fixtures.LoadJson("BackgroundService.pty-handoff-response-ticket.json")));
        var fileSystem = Substitute.For<IServiceFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        fileSystem.TryCreateExclusiveAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("The sidecar directory vanished."));

        var exception = await Assert
            .That(async () => await new ServicePtyHandoff(fileSystem, _clock).PrepareAsync(
                RegistrationPath(), Registration(server.Endpoint), RequestTimeout, CancellationToken.None))
            .Throws<OpenCodeServerException>();

        await Assert.That(exception!.InnerException).IsTypeOf<IOException>();
    }

    [SlopwatchSuppress(
        "SW004",
        "Bounded rendezvous wait: the delay is the timeout guard on Task.WhenAny with the server's arrival signal, so a hung daemon double fails the wait instead of hanging the test.")]
    [Test]
    public async Task PrepareAsync_Should_Return_When_A_Concurrent_Caller_Published_A_Fresh_Matching_Sidecar()
    {
        var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = LoopbackHttpServer.Start(_ => Pending(arrived));
        var registration = Registration(server.Endpoint);

        var pending = Handoff().PrepareAsync(RegistrationPath(), registration, RequestTimeout, CancellationToken.None);
        await Task.WhenAny(arrived.Task, Task.Delay(RequestTimeout));
        await Assert.That(arrived.Task.IsCompleted).IsTrue();
        Seed(SidecarPath(), Sidecar(registration, Ticket, NowMilliseconds + 60_000).ToUtf8Json());
        server.ReleaseResponses();

        await pending;

        await Assert.That(ReadSidecar()!.Handoff).IsNotNull();
    }

    [Test]
    public async Task EnvironmentAsync_Should_Adopt_A_Fresh_Matching_Sidecar()
    {
        Seed(RegistrationPath(), ServiceRegistrationData.Passwordless);
        Seed(SidecarPath(), Sidecar(FixedRegistration(), Ticket, NowMilliseconds + 60_000).ToUtf8Json());

        var overlay = await Handoff().EnvironmentAsync(RegistrationPath(), Caller(), CancellationToken.None);

        await Assert.That(overlay["OPENCODE_PTY_HANDOFF"]).IsEqualTo(Ticket.GetRawText());
        await Assert.That(overlay["KEEP"]).IsEqualTo("1");
    }

    [Test]
    public async Task EnvironmentAsync_Should_Adopt_When_The_Registration_Is_Absent()
    {
        Seed(SidecarPath(), Sidecar(FixedRegistration(), Ticket, NowMilliseconds + 60_000).ToUtf8Json());

        var overlay = await Handoff().EnvironmentAsync(RegistrationPath(), callerEnvironment: null, CancellationToken.None);

        await Assert.That(overlay["OPENCODE_PTY_HANDOFF"]).IsEqualTo(Ticket.GetRawText());
    }

    [Test]
    public async Task EnvironmentAsync_Should_Remove_The_Variable_When_The_Sidecar_Is_Expired()
    {
        Seed(RegistrationPath(), ServiceRegistrationData.Passwordless);
        Seed(SidecarPath(), Sidecar(FixedRegistration(), Ticket, NowMilliseconds - 1).ToUtf8Json());

        var overlay = await Handoff().EnvironmentAsync(RegistrationPath(), Caller(), CancellationToken.None);

        await Assert.That(overlay["OPENCODE_PTY_HANDOFF"]).IsNull();
    }

    [Test]
    public async Task EnvironmentAsync_Should_Remove_The_Variable_When_The_Source_Does_Not_Match_The_Registration()
    {
        Seed(RegistrationPath(), ServiceRegistrationData.DuplicatePidResolved);
        Seed(SidecarPath(), Sidecar(FixedRegistration(), Ticket, NowMilliseconds + 60_000).ToUtf8Json());

        var overlay = await Handoff().EnvironmentAsync(RegistrationPath(), Caller(), CancellationToken.None);

        await Assert.That(overlay["OPENCODE_PTY_HANDOFF"]).IsNull();
    }

    [Test]
    public async Task EnvironmentAsync_Should_Remove_The_Variable_When_The_Sidecar_Is_Absent()
    {
        var overlay = await Handoff().EnvironmentAsync(RegistrationPath(), Caller(), CancellationToken.None);

        await Assert.That(overlay["OPENCODE_PTY_HANDOFF"]).IsNull();
        await Assert.That(overlay["KEEP"]).IsEqualTo("1");
    }

    [Test]
    public async Task EnvironmentAsync_Should_Replace_A_Caller_Provided_Handoff_Value()
    {
        Seed(RegistrationPath(), ServiceRegistrationData.Passwordless);
        Seed(SidecarPath(), Sidecar(FixedRegistration(), Ticket, NowMilliseconds + 60_000).ToUtf8Json());
        var caller = new Dictionary<string, string>(StringComparer.Ordinal) { ["OPENCODE_PTY_HANDOFF"] = "stale" };

        var overlay = await Handoff().EnvironmentAsync(RegistrationPath(), caller, CancellationToken.None);

        await Assert.That(overlay["OPENCODE_PTY_HANDOFF"]).IsEqualTo(Ticket.GetRawText());
    }

    [Test]
    public async Task CompleteAsync_Should_Clear_A_Foreign_Source_Sidecar()
    {
        Seed(SidecarPath(), Sidecar(FixedRegistration(), Ticket, NowMilliseconds + 60_000).ToUtf8Json());
        var winner = new ServiceRegistration(SourceId, Version, FixedUrl, new Uri(FixedUrl), RegisteredPid + 1, Password);

        await Handoff().CompleteAsync(RegistrationPath(), winner, CancellationToken.None);

        await Assert.That(ReadSidecar()).IsNull();
    }

    [Test]
    public async Task CompleteAsync_Should_Keep_A_Same_Source_Sidecar_Even_When_Stale()
    {
        Seed(SidecarPath(), Sidecar(FixedRegistration(), Ticket, NowMilliseconds - 1).ToUtf8Json());

        await Handoff().CompleteAsync(RegistrationPath(), FixedRegistration(), CancellationToken.None);

        await Assert.That(ReadSidecar()).IsNotNull();
    }

    [Test]
    public async Task CompleteAsync_Should_Do_Nothing_When_The_Sidecar_Is_Absent()
    {
        await Handoff().CompleteAsync(RegistrationPath(), FixedRegistration(), CancellationToken.None);

        await Assert.That(ReadSidecar()).IsNull();
    }

    [Test]
    public async Task ClearAsync_Should_Remove_The_Sidecar_And_Be_Idempotent()
    {
        Seed(SidecarPath(), Sidecar(FixedRegistration(), null, NowMilliseconds + 60_000).ToUtf8Json());

        await Handoff().ClearAsync(RegistrationPath(), CancellationToken.None);
        await Assert.That(ReadSidecar()).IsNull();

        await Handoff().ClearAsync(RegistrationPath(), CancellationToken.None);
    }

    [Test]
    public async Task ClearAsync_Should_Report_A_Sidecar_That_Cannot_Be_Removed()
    {
        var fileSystem = Substitute.For<IServiceFileSystem>();
        fileSystem.TryDelete(SidecarPath()).Throws(new UnauthorizedAccessException("The sidecar is locked."));

        var exception = await Assert
            .That(async () => await new ServicePtyHandoff(fileSystem, _clock).ClearAsync(RegistrationPath(), CancellationToken.None))
            .Throws<OpenCodeServerException>();

        await Assert.That(exception!.InnerException).IsTypeOf<UnauthorizedAccessException>();
    }

    [Test]
    public async Task PrepareAsync_Should_Rethrow_Caller_Cancellation()
    {
        var cancelled = new CancellationToken(canceled: true);

        _ = await Assert
            .That(async () => await Handoff().PrepareAsync(RegistrationPath(), FixedRegistration(), RequestTimeout, cancelled))
            .Throws<OperationCanceledException>();
    }

    private static JsonElement LoadTicket()
    {
        using var document = JsonDocument.Parse(Fixtures.LoadJson("BackgroundService.pty-handoff-valid.json"));
        return document.RootElement.GetProperty("handoff").Clone();
    }

    private async Task PrepareAsync(Uri endpoint) =>
        await Handoff().PrepareAsync(RegistrationPath(), Registration(endpoint), RequestTimeout, CancellationToken.None);

    private ServicePtyHandoff Handoff() => new(new TestablyServiceFileSystem(_fileSystem), _clock);

    private static ServiceRegistration Registration(Uri endpoint) =>
        new(SourceId, Version, endpoint.ToString(), endpoint, RegisteredPid, Password);

    private static ServiceRegistration FixedRegistration() =>
        new(SourceId, Version, FixedUrl, new Uri(FixedUrl), RegisteredPid, Password);

    private static PtyHandoffSidecar Sidecar(ServiceRegistration registration, JsonElement? handoff, double expiresAt) =>
        new()
        {
            SourceId = registration.Id,
            SourcePid = registration.ProcessId,
            SourceUrl = registration.Url,
            Handoff = handoff,
            ExpiresAt = expiresAt,
        };

    private static Dictionary<string, string> Caller() =>
        new(StringComparer.Ordinal) { ["KEEP"] = "1" };

    private static LoopbackHttpResponse Pending(TaskCompletionSource<bool> arrived)
    {
        _ = arrived.TrySetResult(true);
        return new LoopbackHttpResponse
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            ContentType = "application/json",
            Body = "{}",
            KeepOpen = true,
        };
    }

    private static LoopbackHttpResponse Json(HttpStatusCode status, string body) =>
        new() { StatusCode = status, ContentType = "application/json", Body = body };

    private static LoopbackHttpResponse NoContent() => new() { StatusCode = HttpStatusCode.NoContent };

    private string RegistrationPath() =>
        _fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(), "service-handoff", "service.json");

    private string SidecarPath() => RegistrationPath() + ServicePtyHandoff.SidecarSuffix;

    private PtyHandoffSidecar? ReadSidecar()
    {
        if (!_fileSystem.File.Exists(SidecarPath()))
        {
            return null;
        }

        // The net472 leg compiles the netstandard2.0 Testably asset, which has no async overloads;
        // the read is arrangement over an in-memory fake, so the synchronous member is the shape
        // every test leg shares (the ServiceEnsurerTests.Seed precedent).
#pragma warning disable MA0045
        return PtyHandoffSidecar.TryRead(_fileSystem.File.ReadAllBytes(SidecarPath()));
#pragma warning restore MA0045
    }

    private IReadOnlyList<string> TemporaryFiles() =>
    [
        .. _fileSystem.Directory
            .GetFiles(_fileSystem.Path.GetDirectoryName(SidecarPath())!)
            .Where(static path => path.EndsWith(".tmp", StringComparison.Ordinal)),
    ];

    private void EnsureSidecarDirectory() =>
        _ = _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(SidecarPath())!);

    private void Seed(string path, string content) => Seed(path, Encoding.UTF8.GetBytes(content));

    private void Seed(string path, byte[] bytes)
    {
        _ = _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(path)!);
        // Same net472 Testably constraint as ReadSidecar.
#pragma warning disable MA0045
        _fileSystem.File.WriteAllBytes(path, bytes);
#pragma warning restore MA0045
    }
}
