using System.Net.WebSockets;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>One step of a scripted terminal WebSocket receive sequence: a frame, a close, a fault, or a park.</summary>
internal sealed record ScriptedTerminalReceive
{
    /// <summary>Gets the message type the step reports.</summary>
    public WebSocketMessageType MessageType { get; init; } = WebSocketMessageType.Text;

    /// <summary>Gets the bytes the step copies into the caller's buffer.</summary>
    public byte[] Payload { get; init; } = [];

    /// <summary>Gets whether the step completes the message it belongs to.</summary>
    public bool EndOfMessage { get; init; } = true;

    /// <summary>Gets the close status a close step reports on the socket.</summary>
    public WebSocketCloseStatus? CloseStatus { get; init; }

    /// <summary>Gets the close description a close step reports on the socket.</summary>
    public string? CloseStatusDescription { get; init; }

    /// <summary>Gets the failure the step throws instead of answering.</summary>
    public Exception? Fault { get; init; }

    /// <summary>Gets whether the step parks until the socket is disposed or the read is canceled.</summary>
    public bool Parks { get; init; }
}
