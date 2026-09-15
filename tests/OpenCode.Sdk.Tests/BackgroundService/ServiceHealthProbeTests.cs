using System.Net;
using System.Text;
using OpenCode.Sdk.Internal.BackgroundService;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// The raw authenticated health exchange the pinned client performs (<c>probeResult</c>): the
/// root-relative path, the Basic credential, the status-to-state mapping for a modern body, the
/// pid and version gates, the legacy refusal, and the separation of the internal bound from the
/// caller's token, all against the loopback server through the SDK's own owned handler.
/// </summary>
public sealed class ServiceHealthProbeTests
{
    private const string Password = "s3cr3t-p455w0rd";
    private static readonly ServiceTiming FastTiming = ServiceTiming.Default with { RequestTimeout = TimeSpan.FromMilliseconds(100) };

    [Test]
    public async Task ProbeAsync_Should_Report_A_Ready_Service_And_Send_The_Basic_Credential()
    {
        await using var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.OK, ServiceHealthBodyData.Ready));

        var result = await Probe().ProbeAsync(Registration(server.Endpoint), CancellationToken.None);

        await Assert.That(result.State).IsEqualTo(ServiceState.Ready);
        await Assert.That(result.Version).IsEqualTo(ServiceHealthBodyData.Version);
        await Assert.That(result.TimedOut).IsFalse();
        var request = server.Requests.Single();
        await Assert.That(request.Method).IsEqualTo("GET");
        await Assert.That(request.Path).IsEqualTo("/api/health");
        await Assert.That(request.Headers["Authorization"])
            .IsEqualTo("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + Password)));
    }

    [Test]
    public async Task ProbeAsync_Should_Resolve_The_Health_Path_Against_The_Authority_Only()
    {
        await using var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.OK, ServiceHealthBodyData.Ready));
        var prefixed = new Uri(server.Endpoint, "/some/prefix/");

        var result = await Probe().ProbeAsync(Registration(prefixed), CancellationToken.None);

        await Assert.That(result.State).IsEqualTo(ServiceState.Ready);
        await Assert.That(server.RequestPaths).IsEquivalentTo(["/api/health"]);
    }

    [Test]
    public async Task ProbeAsync_Should_Send_No_Credential_For_A_Passwordless_Registration()
    {
        await using var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.OK, ServiceHealthBodyData.Ready));

        _ = await Probe().ProbeAsync(Registration(server.Endpoint, password: null), CancellationToken.None);

        await Assert.That(server.Requests.Single().Headers.ContainsKey("Authorization")).IsFalse();
    }

    [Test]
    [Arguments(HttpStatusCode.InternalServerError, (int)ServiceState.Failed)]
    [Arguments(HttpStatusCode.ServiceUnavailable, (int)ServiceState.Waiting)]
    [Arguments(HttpStatusCode.Accepted, (int)ServiceState.Ready)]
    public async Task ProbeAsync_Should_Map_The_Status_Of_A_Modern_Body(HttpStatusCode status, int expected)
    {
        await using var server = LoopbackHttpServer.Start(_ => Json(status, ServiceHealthBodyData.Ready));

        var result = await Probe().ProbeAsync(Registration(server.Endpoint), CancellationToken.None);

        // The enum is internal, so the data row carries its integer; the cast is the assertion's.
        await Assert.That(result.State).IsEqualTo((ServiceState)expected);
    }

    [Test]
    public async Task ProbeAsync_Should_Report_A_Redirect_With_A_Modern_Body_As_Waiting_Without_Following_It()
    {
        await using var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.Found, ServiceHealthBodyData.Ready) with { Location = "/elsewhere" });

        var result = await Probe().ProbeAsync(Registration(server.Endpoint), CancellationToken.None);

        await Assert.That(result.State).IsEqualTo(ServiceState.Waiting);
        await Assert.That(server.RequestPaths).IsEquivalentTo(["/api/health"]);
    }

    [Test]
    [Arguments(ServiceHealthBodyData.Unhealthy)]
    [Arguments(ServiceHealthBodyData.Legacy)]
    [Arguments(ServiceHealthBodyData.LegacyWithVersion)]
    [Arguments(ServiceHealthBodyData.OtherPid)]
    [Arguments(ServiceHealthBodyData.OtherVersion)]
    [Arguments(ServiceHealthBodyData.PidAboveInt32)]
    [Arguments(ServiceHealthBodyData.PidAboveInt64)]
    [Arguments(ServiceHealthBodyData.Malformed)]
    [Arguments(ServiceHealthBodyData.ArrayRoot)]
    [Arguments("")]
    public async Task ProbeAsync_Should_Report_No_Service_For_A_Body_That_Is_Not_This_Daemon(string body)
    {
        await using var server = LoopbackHttpServer.Start(_ => Json(HttpStatusCode.OK, body));

        var result = await Probe().ProbeAsync(Registration(server.Endpoint), CancellationToken.None);

        await Assert.That(result.IsService).IsFalse();
        await Assert.That(result.TimedOut).IsFalse();
    }

    [Test]
    public async Task ProbeAsync_Should_Accept_A_Registration_Without_A_Version()
    {
        await using var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.OK, ServiceHealthBodyData.OtherVersion));

        var result = await Probe().ProbeAsync(Registration(server.Endpoint, version: null), CancellationToken.None);

        await Assert.That(result.State).IsEqualTo(ServiceState.Ready);
        await Assert.That(result.Version).IsEqualTo("0.0.0-other");
    }

    [Test]
    public async Task ProbeAsync_Should_Compare_The_Unknown_Version_Literal_As_A_String()
    {
        await using var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.OK, ServiceHealthBodyData.UnknownVersion));

        var matched = await Probe().ProbeAsync(Registration(server.Endpoint, version: "unknown"), CancellationToken.None);
        var mismatched = await Probe().ProbeAsync(Registration(server.Endpoint, version: "2.0.3"), CancellationToken.None);

        await Assert.That(matched.State).IsEqualTo(ServiceState.Ready);
        await Assert.That(mismatched.IsService).IsFalse();
    }

    [Test]
    public async Task ProbeAsync_Should_Report_No_Service_For_An_Unauthorized_Answer()
    {
        await using var server = LoopbackHttpServer.Start(static _ => new LoopbackHttpResponse { StatusCode = HttpStatusCode.Unauthorized });

        var result = await Probe().ProbeAsync(Registration(server.Endpoint), CancellationToken.None);

        await Assert.That(result.IsService).IsFalse();
        await Assert.That(result.TimedOut).IsFalse();
    }

    [Test]
    public async Task ProbeAsync_Should_Report_No_Service_When_Nothing_Listens()
    {
        Uri endpoint;
        await using (var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.OK, ServiceHealthBodyData.Ready)))
        {
            endpoint = server.Endpoint;
        }

        // Whether a closed loopback port refuses at once or stalls until the bound is the host's
        // business (Windows was observed to stall); the contract is no service, no exception,
        // inside the bound.
        var result = await new ServiceHealthProbe(FastTiming).ProbeAsync(Registration(endpoint), CancellationToken.None);

        await Assert.That(result.IsService).IsFalse();
    }

    [Test]
    public async Task ProbeAsync_Should_Report_A_Timeout_At_The_Injected_Bound()
    {
        await using var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.OK, ServiceHealthBodyData.Ready) with { KeepOpen = true });

        var result = await new ServiceHealthProbe(FastTiming).ProbeAsync(Registration(server.Endpoint), CancellationToken.None);

        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(result.IsService).IsFalse();
        server.ReleaseResponses();
    }

    [Test]
    public async Task ProbeAsync_Should_Rethrow_Caller_Cancellation_Rather_Than_Report_A_Timeout()
    {
        await using var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.OK, ServiceHealthBodyData.Ready) with { KeepOpen = true });
        var cancelled = new CancellationToken(canceled: true);

        var exception = await Assert
            .That(async () => _ = await Probe().ProbeAsync(Registration(server.Endpoint), cancelled))
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancelled);
        await Assert.That(exception.ToString()).DoesNotContain(Password);
        server.ReleaseResponses();
    }

    [Test]
    public async Task The_Result_Should_Never_Render_The_Credential()
    {
        await using var server = LoopbackHttpServer.Start(static _ => Json(HttpStatusCode.OK, ServiceHealthBodyData.Ready));

        var result = await Probe().ProbeAsync(Registration(server.Endpoint), CancellationToken.None);

        await Assert.That(result.ToString()).DoesNotContain(Password);
        await Assert.That(result.ToString()).DoesNotContain("Basic");
    }

    private static ServiceHealthProbe Probe() => new(ServiceTiming.Default);

    private static ServiceRegistration Registration(Uri endpoint, string? version = ServiceHealthBodyData.Version, string? password = Password) =>
        new("srv_1", version, endpoint.ToString(), endpoint, ServiceHealthBodyData.Pid, password);

    private static LoopbackHttpResponse Json(HttpStatusCode status, string body) =>
        new() { StatusCode = status, ContentType = "application/json", Body = body };
}
