using System.Net.WebSockets;
using System.Text.Json;
using OpenCode.Sdk.Internal.Serialization;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Reads one assembled persistent-PTY message. Knowledge source: upstream-observed — binary
/// frames are raw terminal bytes; text frames are JSON objects whose <c>type</c> names one of the
/// seven control kinds. An unrecognized type is carried, not refused (the socket is declared
/// experimental); a body that is not a JSON object with a string <c>type</c> is a protocol
/// failure, and so is a frame whose type is known but whose members are not readable. The two
/// are reported apart because they mean different things to whoever reads the message: one says
/// the wire is not this protocol at all, the other names the frame the SDK could not read. The
/// decoder holds no state, so one instance serves every session.
/// </summary>
internal sealed class PersistentPtyFrameDecoder : ITerminalFrameDecoder<PersistentPtyFrame>
{
    private const string AttachedType = "attached";

    private const string ControlFrameFailure =
        "The opencode server sent a persistent PTY control frame whose body is not a JSON object carrying a string 'type'.";

    private PersistentPtyFrameDecoder()
    {
    }

    /// <summary>Gets the shared decoder instance.</summary>
    public static PersistentPtyFrameDecoder Instance { get; } = new();

    /// <inheritdoc />
    public PersistentPtyFrame Decode(WebSocketMessageType messageType, byte[] payload, int count)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (messageType is WebSocketMessageType.Binary)
        {
            // The receive buffer is reused by the next message, so the frame owns a copy.
            return new PersistentPtyOutputFrame(new ReadOnlyMemory<byte>(payload.AsSpan(0, count).ToArray()));
        }

        using var document = ParseControlBody(payload, count);
        var root = document.RootElement;
        if (root.ValueKind is not JsonValueKind.Object
            || !root.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind is not JsonValueKind.String)
        {
            throw new OpenCodeTransportException(ControlFrameFailure);
        }

        var type = typeElement.GetString()!;
        try
        {
            return Read(root, type);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new OpenCodeTransportException(TypedFrameFailure(type), exception);
        }
    }

    /// <summary>Names a frame whose type is known but whose members the SDK could not read.</summary>
    private static string TypedFrameFailure(string type) =>
        $"The opencode server sent a persistent PTY '{type}' control frame the SDK could not read.";

    private static JsonDocument ParseControlBody(byte[] payload, int count)
    {
        try
        {
            return JsonDocument.Parse(new ReadOnlyMemory<byte>(payload, 0, count));
        }
        catch (JsonException exception)
        {
            // Nothing is known about the body yet, not even a type to name it by.
            throw new OpenCodeTransportException(ControlFrameFailure, exception);
        }
    }

    private static PersistentPtyFrame Read(JsonElement root, string? type) =>
        type switch
        {
            AttachedType => new PersistentPtyAttachedFrame(ReadAttachment(root)),
            "replay_complete" => new PersistentPtyReplayCompleteFrame(root.GetProperty("endOffset").GetInt64()),
            "resized" => new PersistentPtyResizedFrame(
                root.GetProperty("cols").GetInt32(),
                root.GetProperty("rows").GetInt32(),
                root.GetProperty("generation").GetInt64(),
                root.GetProperty("checkpoint").GetBytesFromBase64()),
            "exited" => new PersistentPtyExitedFrame(
                root.TryGetProperty("exitCode", out var exitCode) && exitCode.ValueKind is JsonValueKind.Number
                    ? exitCode.GetInt32()
                    : null,
                root.GetProperty("finalOffset").GetInt64()),
            "controller_changed" => new PersistentPtyControllerChangedFrame(
                root.TryGetProperty("attachmentID", out var attachment) && attachment.ValueKind is JsonValueKind.String
                    ? attachment.GetString()
                    : null,
                root.GetProperty("generation").GetInt64()),
            "title_changed" => new PersistentPtyTitleChangedFrame(root.GetProperty("title").GetString()!),
            "foreground_process_changed" => new PersistentPtyForegroundProcessChangedFrame(
                root.GetProperty("process").ValueKind is JsonValueKind.String
                    ? root.GetProperty("process").GetString()
                    : null),

            // The element is cloned because the frame outlives the document it was parsed from.
            var other => new PersistentPtyUnknownFrame(other!, root.Clone()),
        };

    private static PersistentPtyAttachment ReadAttachment(JsonElement root) =>
        new()
        {
            AttachmentId = root.GetProperty("attachmentID").GetString()!,
            InputProtocol = root.GetProperty("inputProtocol").GetInt32(),

            // The source-generated type info, never reflection: the SDK stays trimming- and
            // AOT-safe on every target. A wire null is a member-level failure of a frame whose
            // type is already known, so it is named as one.
            Info = root.GetProperty("info").Deserialize(OpenCodeJsonContext.Default.PersistentPtyInfo)
                   ?? throw new OpenCodeTransportException(TypedFrameFailure(AttachedType)),
            Role = string.Equals(root.GetProperty("role").GetString(), "observer", StringComparison.Ordinal)
                ? PersistentPtyRole.Observer
                : PersistentPtyRole.Controller,
            Generation = root.GetProperty("generation").GetInt64(),
            Replay = ReadReplay(root.GetProperty("replay")),
        };

    private static PersistentPtyReplayBounds ReadReplay(JsonElement replay) =>
        new(
            replay.GetProperty("requestedOffset").GetInt64(),
            replay.GetProperty("availableOffset").GetInt64(),
            replay.GetProperty("endOffset").GetInt64(),
            replay.GetProperty("truncated").GetBoolean());
}
