using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Owns the loopback handshake that validates a nonce and captures the Bun root and child as live
/// process handles before acknowledging the startup peer.
/// </summary>
internal sealed class ServerStartupTreeHandshake : IDisposable
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

    private readonly Task _observation;
    private readonly TcpListener _listener;
    private Process? _childProcess;
    private Process? _rootProcess;

    public ServerStartupTreeHandshake(CancellationToken cancellationToken)
    {
        Nonce = Guid.NewGuid().ToString("N");
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _observation = CaptureAndAcknowledgeAsync(cancellationToken);
    }

    public Process ChildProcess =>
        _childProcess ?? throw new InvalidOperationException("The child process has not been captured.");

    public Process? CapturedChildProcess => _childProcess;

    public Process? CapturedRootProcess => _rootProcess;

    public string Nonce { get; }

    public int Port { get; }

    public Process RootProcess =>
        _rootProcess ?? throw new InvalidOperationException("The root process has not been captured.");

    public async Task ObserveAndAcknowledgeAsync(CancellationToken cancellationToken)
    {
        var observation = _observation;
        await observation.WaitAsync(cancellationToken);
    }

    public async Task AwaitCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var observation = _observation;
            await observation.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            _ = exception;
        }
        catch (ObjectDisposedException exception)
        {
            _ = exception;
        }
        catch (SocketException exception)
        {
            _ = exception;
        }
    }

    public void Stop() => _listener.Stop();

    public void Dispose()
    {
        _childProcess?.Dispose();
        _rootProcess?.Dispose();
#if NET
        _listener.Dispose();
#endif
    }

    private async Task CaptureAndAcknowledgeAsync(CancellationToken cancellationToken)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(HandshakeTimeout);
        using var client = await AcceptTcpClientAsync(_listener, bound.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var line = await ReadLineAsync(reader, bound.Token)
                   ?? throw new InvalidOperationException("The child-tree handshake ended before process identity arrived.");
        using var document = JsonDocument.Parse(line);
        var root = CaptureProcess(document.RootElement, "rootPid");
        var transferred = false;
        try
        {
            var child = CaptureProcess(document.RootElement, "childPid");
            try
            {
                var receivedNonce = document.RootElement.GetProperty("nonce").GetString();
                if (!string.Equals(receivedNonce, Nonce, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The child-tree handshake nonce did not match this scenario.");
                }

                if (root.Id == child.Id || root.HasExited || child.HasExited)
                {
                    throw new InvalidOperationException("The child-tree handshake did not identify two live processes.");
                }

                _rootProcess = root;
                _childProcess = child;
                transferred = true;
                await WriteAcknowledgementAsync(stream, bound.Token);
            }
            finally
            {
                if (!transferred)
                {
                    child.Dispose();
                }
            }
        }
        finally
        {
            if (!transferred)
            {
                root.Dispose();
            }
        }
    }

    private static Process CaptureProcess(JsonElement handshake, string propertyName)
    {
        var processId = handshake.GetProperty(propertyName).GetInt32();
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"The child-tree handshake process '{propertyName}' was not alive when captured.", exception);
        }
    }

    private static Task<TcpClient> AcceptTcpClientAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
#if NET
        return listener.AcceptTcpClientAsync(cancellationToken).AsTask();
#else
        return listener.AcceptTcpClientAsync().WaitAsync(cancellationToken);
#endif
    }

    private static Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
#if NET
        return reader.ReadLineAsync(cancellationToken).AsTask();
#else
        return reader.ReadLineAsync().WaitAsync(cancellationToken);
#endif
    }

    private static async Task WriteAcknowledgementAsync(Stream stream, CancellationToken cancellationToken)
    {
        var acknowledgement = Encoding.ASCII.GetBytes("ACK\n");
#if NET
        await stream.WriteAsync(acknowledgement, cancellationToken);
#else
        await stream.WriteAsync(acknowledgement, 0, acknowledgement.Length, cancellationToken);
#endif
        await stream.FlushAsync(cancellationToken);
    }
}
