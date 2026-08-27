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
    public async Task RequestDecorationPolicy_Should_Let_PerCall_Location_Win_Member_By_Member()
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
    public async Task RequestDecorationPolicy_Should_Not_Clear_An_Ambient_Member_When_PerCall_Leaves_It_Null()
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
    public async Task RequestDecorationPolicy_Should_PercentEncode_The_Directory_And_Send_The_Workspace_Raw()
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
        // Pinned literal for Uri.EscapeDataString("/tmp/päth ü"), computed once rather than
        // recomputed inline so the test does not exercise the very API it is pinning.
        await Assert.That(request.Headers["x-opencode-directory"]).IsEqualTo("%2Ftmp%2Fp%C3%A4th%20%C3%BC");
        await Assert.That(request.Headers["x-opencode-workspace"]).IsEqualTo("wsp_123");
    }

    [Test]
    public async Task RequestDecorationPolicy_Should_Send_No_Location_Headers_When_Location_Is_Absent()
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
    public async Task RequestDecorationPolicy_Should_Send_Declared_Headers_Alongside_The_Location_Headers()
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
    public async Task RequestDecorationPolicy_Should_Send_No_Extra_Headers_When_Declared_Headers_Are_Absent()
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
    public async Task RequestDecorationPolicy_Should_Send_Only_The_Set_PerCall_Member_When_Ambient_Is_Absent()
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

    /// <summary>
    /// Pins <see cref="LocationMerge"/>'s query-channel merge and <c>RequestDecorationPolicy</c>'s
    /// header-channel merge to the same result for the same inputs, across a matrix covering both
    /// members set on both sides, only one side set, and neither set. The two sites restate the
    /// same member-by-member rule independently (<c>client-runtime.md</c>'s "Location merge"
    /// note); this test is what keeps a silent drift between them from going unnoticed.
    /// </summary>
    [Test]
    [Arguments("/per/dir", "per-ws", "/amb/dir", "amb-ws")]
    [Arguments("/per/dir", null, "/amb/dir", "amb-ws")]
    [Arguments(null, "per-ws", "/amb/dir", "amb-ws")]
    [Arguments(null, null, "/amb/dir", "amb-ws")]
    [Arguments("/per/dir", "per-ws", null, null)]
    [Arguments(null, null, null, null)]
    [Arguments("/per/dir", null, null, "amb-ws")]
    public async Task LocationMerge_Should_Equal_What_RequestDecorationPolicy_Puts_On_The_Wire(
        string? perCallDirectory, string? perCallWorkspace, string? ambientDirectory, string? ambientWorkspace)
    {
        var perCall = perCallDirectory is null && perCallWorkspace is null
            ? null
            : new LocationSelector { Directory = perCallDirectory, Workspace = perCallWorkspace };
        var ambient = ambientDirectory is null && ambientWorkspace is null
            ? null
            : new LocationSelector { Directory = ambientDirectory, Workspace = ambientWorkspace };
        var expected = LocationMerge.Resolve(perCall, ambient);

        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient, location: ambient);
        var options = perCall is null ? null : new OpenCodeRequestOptions { Location = perCall };

        _ = await pipeline.ExecuteAsync(HttpMethod.Get, "/api/health", new RecordingResponseAdapter(), options, CancellationToken.None);

        var request = handler.Requests.Single();
        var wireDirectory = request.Headers.TryGetValue("x-opencode-directory", out var directoryHeader)
            ? Uri.UnescapeDataString(directoryHeader)
            : null;
        var wireWorkspace = request.Headers.TryGetValue("x-opencode-workspace", out var workspaceHeader)
            ? workspaceHeader
            : null;

        await Assert.That(wireDirectory).IsEqualTo(expected?.Directory);
        await Assert.That(wireWorkspace).IsEqualTo(expected?.Workspace);
    }
}
