using System.Text.Json;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The persistent-terminal handoff sidecar the pinned client keeps at
/// <c>&lt;registration&gt;.pty-handoff</c> (<c>pty-handoff.ts:7-11</c>): the source identity
/// (<c>id</c>, <c>pid</c>, <c>url</c>), the validated handoff ticket exactly as the route answered
/// it (or a JSON null), and an expiry in epoch milliseconds. The type owns its own focused
/// <see cref="Utf8JsonReader"/>/<see cref="Utf8JsonWriter"/> boundary — no serializer context —
/// and its reader follows upstream's strict shape guard: any document that is not this shape reads
/// as absent, never as a failure. The handoff payload is retained as a
/// <see cref="JsonElement"/> so unknown members survive a read/write round trip and the value can
/// ride <c>OPENCODE_PTY_HANDOFF</c> verbatim; it is deliberately excluded from
/// <see cref="ToString"/> because a ticket is secret-bearing.
/// </summary>
internal sealed record PtyHandoffSidecar
{
    /// <summary>Gets the daemon's instance id, when the source published one.</summary>
    public required string? SourceId { get; init; }

    /// <summary>Gets the source pid; the reader admits only an integral JSON number.</summary>
    public required int SourcePid { get; init; }

    /// <summary>Gets the source url exactly as a JSON string.</summary>
    public required string SourceUrl { get; init; }

    /// <summary>Gets the handoff ticket, or <see langword="null"/> when the sidecar carries a JSON null.</summary>
    public required JsonElement? Handoff { get; init; }

    /// <summary>Gets the sidecar expiry in epoch milliseconds.</summary>
    public required double ExpiresAt { get; init; }

    /// <summary>Decodes a sidecar document.</summary>
    /// <param name="utf8">The file's bytes.</param>
    /// <returns>The sidecar, or null for any document that is not this shape.</returns>
    public static PtyHandoffSidecar? TryRead(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return Read(utf8);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Encodes the sidecar the way the pinned client's <c>publish</c> writes it: the source object
    /// (the id member omitted when absent, as <c>JSON.stringify</c> omits <c>undefined</c>), the
    /// handoff value, and the expiry.
    /// </summary>
    /// <returns>The UTF-8 document.</returns>
    public byte[] ToUtf8Json()
    {
        // Utf8JsonWriter implements IAsyncDisposable, which is what MA0045 reacts to, but this is a
        // pure in-memory buffering composition with nothing to await (the DriveProtocol.Compose
        // precedent); the method's contract is the synchronous byte[] the publisher consumes.
#pragma warning disable MA0045
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("source");
            if (SourceId is { } id)
            {
                writer.WriteString("id", id);
            }

            writer.WriteNumber("pid", SourcePid);
            writer.WriteString("url", SourceUrl);
            writer.WriteEndObject();
            writer.WritePropertyName("handoff");
            if (Handoff is { } handoff)
            {
                handoff.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteNumber("expiresAt", ExpiresAt);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
#pragma warning restore MA0045
    }

    /// <summary>
    /// Returns a fixed redacted rendering: the handoff ticket is secret-bearing and must never
    /// reach a diagnostic surface.
    /// </summary>
    /// <returns>The redacted rendering.</returns>
    public override string ToString() => "PtyHandoffSidecar { Handoff = <redacted> }";

    private static PtyHandoffSidecar? Read(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        var seen = Members.None;
        string? sourceId = null;
        var sourcePid = 0;
        string? sourceUrl = null;
        JsonElement? handoff = null;
        var expiresAt = 0d;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("source"u8))
            {
                if ((seen & Members.Source) != Members.None || !ReadSource(ref reader, out sourceId, out sourcePid, out sourceUrl))
                {
                    return null;
                }

                seen |= Members.Source;
                continue;
            }

            if (reader.ValueTextEquals("handoff"u8))
            {
                if ((seen & Members.Handoff) != Members.None || !ReadHandoff(ref reader, out handoff))
                {
                    return null;
                }

                seen |= Members.Handoff;
                continue;
            }

            if (reader.ValueTextEquals("expiresAt"u8))
            {
                if ((seen & Members.ExpiresAt) != Members.None || !ReadExpiry(ref reader, out expiresAt))
                {
                    return null;
                }

                seen |= Members.ExpiresAt;
                continue;
            }

            reader.Skip();
        }

        if (reader.TokenType != JsonTokenType.EndObject ||
            (seen & (Members.Source | Members.Handoff | Members.ExpiresAt)) !=
            (Members.Source | Members.Handoff | Members.ExpiresAt))
        {
            return null;
        }

        return new PtyHandoffSidecar
        {
            SourceId = sourceId,
            SourcePid = sourcePid,
            SourceUrl = sourceUrl!,
            Handoff = handoff,
            ExpiresAt = expiresAt,
        };
    }

    private static bool ReadSource(ref Utf8JsonReader reader, out string? id, out int pid, out string? url)
    {
        id = null;
        pid = 0;
        url = null;
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        var seen = Members.None;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("pid"u8))
            {
                if ((seen & Members.Pid) != Members.None ||
                    !reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out pid))
                {
                    return false;
                }

                seen |= Members.Pid;
                continue;
            }

            if (reader.ValueTextEquals("url"u8))
            {
                if ((seen & Members.Url) != Members.None || !reader.Read() || reader.TokenType != JsonTokenType.String)
                {
                    return false;
                }

                seen |= Members.Url;
                url = reader.GetString();
                continue;
            }

            if (reader.ValueTextEquals("id"u8))
            {
                if ((seen & Members.Id) != Members.None || !reader.Read() || reader.TokenType != JsonTokenType.String)
                {
                    return false;
                }

                seen |= Members.Id;
                id = reader.GetString();
                continue;
            }

            reader.Skip();
        }

        return reader.TokenType == JsonTokenType.EndObject &&
            (seen & (Members.Pid | Members.Url)) == (Members.Pid | Members.Url);
    }

    private static bool ReadHandoff(ref Utf8JsonReader reader, out JsonElement? handoff)
    {
        handoff = null;
        if (!reader.Read())
        {
            return false;
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return true;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement.Clone();
        if (!IsHandoff(element))
        {
            return false;
        }

        handoff = element;
        return true;
    }

    private static bool ReadExpiry(ref Utf8JsonReader reader, out double value)
    {
        value = 0;
        return reader.Read() &&
            reader.TokenType == JsonTokenType.Number &&
            reader.TryGetDouble(out value) &&
            IsFinite(value);
    }

    /// <summary>The pinned client's <c>isHandoff</c> shape guard, unknown members ignored.</summary>
    private static bool IsHandoff(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        HasString(element, "directory") &&
        HasString(element, "instanceID") &&
        HasString(element, "ticket") &&
        element.TryGetProperty("expiresAt", out var expiresAt) &&
        expiresAt.ValueKind == JsonValueKind.Number &&
        expiresAt.TryGetDouble(out var value) &&
        IsFinite(value);

    /// <summary><c>Number.isFinite</c>: finite on every target framework, unlike <c>double.IsFinite</c>.</summary>
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool HasString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String;

    [Flags]
    private enum Members
    {
        None = 0,
        Source = 1,
        Handoff = 2,
        ExpiresAt = 4,
        Id = 8,
        Pid = 16,
        Url = 32,
    }
}
