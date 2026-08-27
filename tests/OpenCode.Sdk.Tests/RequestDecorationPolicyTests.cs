using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// Exercises <c>RequestDecorationPolicy</c>'s per-call/ambient location merge and its
/// document-declared header channel through a real <see cref="Pipeline"/> and a
/// <see cref="RecordingHttpHandler"/>, asserting the headers actually sent on the wire.
/// </summary>
public sealed class RequestDecorationPolicyTests
{
    [Test]
    public async Task PerCallLocationOverridesAmbientMemberByMember()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient, location: new LocationSelector
        {
            Directory = "/amb/dir",
            Workspace = "amb-ws",
        });
        var options = new OpenCodeRequestOptions
        {
            Location = new LocationSelector { Directory = "/per/dir" },
        };

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options, CancellationToken.None);

        var request = handler.Requests.Single();
        await Assert.That(request.Headers["x-opencode-directory"]).IsEqualTo("%2Fper%2Fdir");
        await Assert.That(request.Headers["x-opencode-workspace"]).IsEqualTo("amb-ws");
    }

    [Test]
    public async Task PerCallLocationCannotClearAnAmbientMember()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient, location: new LocationSelector
        {
            Directory = "/amb/dir",
            Workspace = "amb-ws",
        });

        // LocationSelector refuses blank members, so the only spelling of "clear" is null —
        // and null inherits. A per-call selector with both members left null must not clear
        // either ambient header.
        var options = new OpenCodeRequestOptions
        {
            Location = new LocationSelector(),
        };

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options, CancellationToken.None);

        var request = handler.Requests.Single();
        await Assert.That(request.Headers["x-opencode-directory"]).IsEqualTo("%2Famb%2Fdir");
        await Assert.That(request.Headers["x-opencode-workspace"]).IsEqualTo("amb-ws");
    }

    [Test]
    public async Task PerCallDirectoryIsPercentEncodedAndWorkspaceRidesRaw()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);
        var options = new OpenCodeRequestOptions
        {
            Location = new LocationSelector
            {
                Directory = "/tmp/päth ü",
                Workspace = "wsp_123",
            },
        };

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options, CancellationToken.None);

        var request = handler.Requests.Single();
        await Assert.That(request.Headers["x-opencode-directory"]).IsEqualTo(Uri.EscapeDataString("/tmp/päth ü"));
        await Assert.That(request.Headers["x-opencode-workspace"]).IsEqualTo("wsp_123");
    }

    [Test]
    public async Task AbsentLocationsSendNoLocationHeaders()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options: null, CancellationToken.None);

        var request = handler.Requests.Single();
        await Assert.That(request.Headers.ContainsKey("x-opencode-directory")).IsFalse();
        await Assert.That(request.Headers.ContainsKey("x-opencode-workspace")).IsFalse();
    }

    [Test]
    public async Task DeclaredHeadersRideAlongsideTheLocationHeaders()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient, location: new LocationSelector { Directory = "/amb/dir" });

        // The policy is told a name and a value and nothing else; it must write whatever it is
        // handed without recognizing the operation or the header.
        _ = await pipeline.ExecuteAsync(
            HttpMethod.Post,
            "/api/health",
            new RecordingResponseAdapter(),
            options: null,
            [new DeclaredHeader("x-probe-one", "first"), new DeclaredHeader("x-probe-two", "second")],
            CancellationToken.None);

        var request = handler.Requests.Single();
        await Assert.That(request.Headers["x-probe-one"]).IsEqualTo("first");
        await Assert.That(request.Headers["x-probe-two"]).IsEqualTo("second");
        await Assert.That(request.Headers["x-opencode-directory"]).IsEqualTo("%2Famb%2Fdir");
    }

    [Test]
    public async Task AnAbsentDeclaredHeaderListSendsNoExtraHeaders()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);

        _ = await pipeline.ExecuteAsync(
            HttpMethod.Post,
            "/api/health",
            new RecordingResponseAdapter(),
            options: null,
            declaredHeaders: null,
            CancellationToken.None);

        var request = handler.Requests.Single();
        await Assert.That(request.Headers.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(["User-Agent"]);
    }

    [Test]
    public async Task PerCallLocationWithoutAmbientSendsOnlySetMembers()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);
        var options = new OpenCodeRequestOptions
        {
            Location = new LocationSelector { Directory = "/per/dir" },
        };

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options, CancellationToken.None);

        var request = handler.Requests.Single();
        await Assert.That(request.Headers["x-opencode-directory"]).IsEqualTo("%2Fper%2Fdir");
        await Assert.That(request.Headers.ContainsKey("x-opencode-workspace")).IsFalse();
    }
}
