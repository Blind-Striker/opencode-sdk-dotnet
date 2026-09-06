using System.Net;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>Serves a healthy external endpoint with either a completed or stalled typed PTY response.</summary>
internal sealed class PtyDiagnosticServer : IAsyncDisposable
{
    private readonly TaskCompletionSource<bool> _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PtyDiagnosticServer(bool stall)
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-pty.json");
        Server = LoopbackHttpServer.Start(path =>
        {
            if (path == "/api/health")
            {
                return new LoopbackHttpResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    ContentType = "application/json",
                    Body = WireBodyData.HealthOk,
                };
            }

            _ = _requested.TrySetResult(true);
            return new LoopbackHttpResponse
            {
                StatusCode = HttpStatusCode.OK,
                ContentType = "application/json",
                Body = WireBodyData.LocationEnvelope(payload),
                KeepOpen = stall,
            };
        });
    }

    public LoopbackHttpServer Server { get; }

    public Task Requested => _requested.Task;

    public async ValueTask DisposeAsync() => await Server.DisposeAsync();
}
