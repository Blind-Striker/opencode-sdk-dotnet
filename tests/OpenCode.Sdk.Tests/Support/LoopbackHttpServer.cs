using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>Serves deterministic HTTP/1.1 responses through the platform's real HTTP handler.</summary>
internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly Task _acceptLoop;
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<string> _requestPaths = [];
    private readonly TaskCompletionSource<object?> _responseRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<string, LoopbackHttpResponse> _respond;
    private readonly CancellationTokenSource _shutdown = new();

    private LoopbackHttpServer(Func<string, LoopbackHttpResponse> respond)
    {
        _respond = respond;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var localEndpoint = (IPEndPoint)_listener.LocalEndpoint;
        Endpoint = new Uri($"http://127.0.0.1:{localEndpoint.Port.ToString(CultureInfo.InvariantCulture)}");
        _acceptLoop = AcceptLoopAsync();
    }

    public Uri Endpoint { get; }

    public IReadOnlyList<string> RequestPaths => [.. _requestPaths];

    public static LoopbackHttpServer Start(Func<string, LoopbackHttpResponse> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        return new LoopbackHttpServer(respond);
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        _listener.Stop();
        Task[] acceptTasks = [_acceptLoop];
        await Task.WhenAll(acceptTasks);
        _ = _responseRelease.TrySetResult(null);
        await Task.WhenAll(_connections);
#if NET
        _listener.Dispose();
#endif
        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception exception) when (_shutdown.IsCancellationRequested && exception is ObjectDisposedException or SocketException)
            {
                break;
            }

            _connections.Add(ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            var path = await ReadRequestPathAsync(stream);
            _requestPaths.Enqueue(path);
            var response = _respond(path);
            await WriteResponseAsync(stream, response);
            if (response.KeepOpen)
            {
                Task[] releaseTasks = [_responseRelease.Task];
                await Task.WhenAll(releaseTasks);
            }
        }
    }

    private static async Task<string> ReadRequestPathAsync(Stream stream)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var requestLine = await reader.ReadLineAsync()
                          ?? throw new InvalidOperationException("The loopback request ended before its request line.");
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
            // Request headers are irrelevant to this transport-policy fixture.
        }

        var parts = requestLine.Split(' ');
        return parts.Length >= 2
            ? parts[1]
            : throw new InvalidOperationException($"Invalid loopback request line '{requestLine}'.");
    }

    private static async Task WriteResponseAsync(Stream stream, LoopbackHttpResponse response)
    {
        var body = Encoding.UTF8.GetBytes(response.Body);
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ")
            .Append(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(ReasonPhrase(response.StatusCode))
            .Append("\r\n");
        if (response.ContentType is not null)
        {
            _ = headers.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        }

        if (response.Location is not null)
        {
            _ = headers.Append("Location: ").Append(response.Location).Append("\r\n");
        }

        if (response.KeepOpen)
        {
            _ = headers.Append("Transfer-Encoding: chunked\r\nConnection: keep-alive\r\n\r\n");
        }
        else
        {
            _ = headers.Append("Content-Length: ")
                .Append(body.Length.ToString(CultureInfo.InvariantCulture))
                .Append("\r\nConnection: close\r\n\r\n");
        }

        var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
        await WriteAsync(stream, headerBytes);
        if (response.KeepOpen)
        {
            var chunkPrefix = Encoding.ASCII.GetBytes(body.Length.ToString("X", CultureInfo.InvariantCulture) + "\r\n");
            await WriteAsync(stream, chunkPrefix);
            await WriteAsync(stream, body);
            await WriteAsync(stream, [13, 10]);
        }
        else
        {
            await WriteAsync(stream, body);
        }

        await stream.FlushAsync();
    }

    private static string ReasonPhrase(HttpStatusCode statusCode) => (int)statusCode switch
    {
        200 => "OK",
        302 => "Found",
        401 => "Unauthorized",
        500 => "Internal Server Error",
        _ => statusCode.ToString(),
    };

    private static Task WriteAsync(Stream stream, byte[] content)
    {
#if NET
        return stream.WriteAsync(content).AsTask();
#else
        return stream.WriteAsync(content, 0, content.Length);
#endif
    }
}
