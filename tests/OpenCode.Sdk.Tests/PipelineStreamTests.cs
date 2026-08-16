using System.Net;
using System.Net.Http.Headers;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class PipelineStreamTests
{
    [Test]
    public async Task ExecuteStreamAsync_Should_Yield_Every_Frame_Payload()
    {
        using var handler = new RecordingHttpHandler(static _ => EventStream(
            "data: {\"value\":\"first\"}\n\ndata: {\"value\":\"second\"}\n\n"));
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);

        var payloads = await CollectAsync(pipeline);

        await Assert.That(payloads.Select(static payload => payload.Value)
            .SequenceEqual(["first", "second"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ExecuteStreamAsync_Should_Decorate_The_Request_Like_Any_Other()
    {
        using var handler = new RecordingHttpHandler(static _ => EventStream("data: {\"value\":\"first\"}\n\n"));
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient, password: "secret",
            location: new LocationSelector { Directory = "/repo" });

        _ = await CollectAsync(pipeline);

        var request = handler.Requests.Single();
        await Assert.That(request.Authorization).IsNotNull();
        await Assert.That(request.UserAgent).IsEqualTo(UserAgentPolicy.Resolve().ToString());
        await Assert.That(request.Headers["x-opencode-directory"]).IsEqualTo("%2Frepo");
    }

    [Test]
    public async Task ExecuteStreamAsync_Should_Throw_The_Typed_Error_Before_Opening_The_Stream()
    {
        using var handler = new RecordingHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(WireBodyData.SessionNotFoundError),
        });
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);

        var exception = await Assert
            .That(async () => _ = await CollectAsync(pipeline, new RecordingStreamAdapter("SessionNotFoundError")))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(404);
        await Assert.That(exception.Error).IsTypeOf<SessionNotFoundError>();
        await Assert.That(exception.RawBody).Contains("SessionNotFoundError");
    }

    [Test]
    public async Task ExecuteStreamAsync_Should_Refuse_A_Success_That_Is_Not_An_Event_Stream()
    {
        using var handler = new RecordingHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"value\":\"first\"}"),
        });
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);

        _ = await Assert
            .That(async () => _ = await CollectAsync(pipeline))
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ExecuteStreamAsync_Should_Refuse_NoThrow_Instead_Of_Ignoring_It()
    {
        using var handler = new RecordingHttpHandler(static _ => EventStream("data: {\"value\":\"first\"}\n\n"));
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);

        var exception = Assert.Throws<ArgumentException>(() => _ = pipeline.ExecuteStreamAsync(
            HttpMethod.Get, "/api/event", new RecordingStreamAdapter(), OpenCodeRequestOptions.NoThrow, CancellationToken.None));

        await Assert.That(exception.ParamName).IsEqualTo("options");
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task ExecuteStreamAsync_Should_Treat_A_Malformed_Frame_As_A_Protocol_Failure()
    {
        using var handler = new RecordingHttpHandler(static _ => EventStream("data: not json\n\n"));
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);

        _ = await Assert
            .That(async () => _ = await CollectAsync(pipeline))
            .Throws<OpenCodeTransportException>();
    }

    [Test]
    public async Task ExecuteStreamAsync_Should_Honor_Cancellation()
    {
        using var handler = new RecordingHttpHandler(static _ => EventStream("data: {\"value\":\"first\"}\n\n"));
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        _ = await Assert
            .That(async () => _ = await CollectAsync(pipeline, cancellationToken: cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ExecuteStreamAsync_Should_Refuse_A_Route_Without_A_Leading_Slash()
    {
        using var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var pipeline = PipelineFactory.Create(httpClient);

        var exception = Assert.Throws<ArgumentException>(() => _ = pipeline.ExecuteStreamAsync(
            HttpMethod.Get, "api/event", new RecordingStreamAdapter(), options: null, CancellationToken.None));

        await Assert.That(exception.ParamName).IsEqualTo("route");
    }

    private static HttpResponseMessage EventStream(string body)
    {
        var content = new StringContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content, };
    }

    private static async Task<List<TestBody>> CollectAsync(Pipeline pipeline,
        RecordingStreamAdapter? adapter = null,
        CancellationToken cancellationToken = default)
    {
        var payloads = new List<TestBody>();
        var stream = pipeline.ExecuteStreamAsync(
            HttpMethod.Get, "/api/event", adapter ?? new RecordingStreamAdapter(), options: null, cancellationToken);
        await foreach (var payload in stream)
        {
            payloads.Add(payload);
        }

        return payloads;
    }
}
