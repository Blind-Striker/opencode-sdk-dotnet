namespace OpenCode.Sdk.Tests.Support;

/// <summary>Canned wire bodies and envelope shapes for the contract tests.</summary>
internal static class WireBodyData
{
    public const string HealthOk = "{\"healthy\":true,\"version\":\"0.0.0-test\",\"pid\":42}";

    public const string UnauthorizedError = "{\"_tag\":\"UnauthorizedError\",\"message\":\"password required\"}";

    public const string SessionNotFoundError = "{\"_tag\":\"SessionNotFoundError\",\"sessionID\":\"ses_9\",\"message\":\"gone\"}";

    public const string MessageNotFoundError = "{\"_tag\":\"MessageNotFoundError\",\"sessionID\":\"ses_9\",\"messageID\":\"msg_1\",\"message\":\"gone\"}";

    public const string UnknownError = "{\"_tag\":\"UnknownError\",\"message\":\"boom\"}";

    public const string InvalidCursorError = "{\"_tag\":\"InvalidCursorError\",\"message\":\"stale\"}";

    public const string InvalidRequestError = "{\"_tag\":\"InvalidRequestError\",\"message\":\"bad\"}";

    public const string ShellNotFoundError = "{\"_tag\":\"ShellNotFoundError\",\"id\":\"sh_9\",\"message\":\"gone\"}";

    public const string ResolvedLocation =
        "{\"directory\":\"/repo\",\"project\":{\"id\":\"prj_1\",\"directory\":\"/repo\",\"canonical\":\"/repo\"}}";

    public static string Envelope(string datum)
    {
        ArgumentNullException.ThrowIfNull(datum);

        return $"{{\"data\":{datum}}}";
    }

    public static string LocationEnvelope(string datum, string location = ResolvedLocation)
    {
        ArgumentNullException.ThrowIfNull(datum);
        ArgumentNullException.ThrowIfNull(location);

        return $"{{\"location\":{location},\"data\":{datum}}}";
    }

    public static string Page(string items, string? previous = null, string? next = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var cursor = (previous, next) switch
        {
            (null, null) => "{}",
            (not null, null) => $"{{\"previous\":\"{previous}\"}}",
            (null, not null) => $"{{\"next\":\"{next}\"}}",
            _ => $"{{\"previous\":\"{previous}\",\"next\":\"{next}\"}}",
        };
        return $"{{\"data\":[{items}],\"cursor\":{cursor}}}";
    }

    /// <summary>One durable session event at envelope version 1, as the log writes it.</summary>
    public const string SessionCreatedEvent =
        "{\"id\":\"evt_1\",\"created\":1,\"type\":\"session.created\","
        + "\"durable\":{\"aggregateID\":\"ses_9\",\"seq\":1,\"version\":1},"
        + "\"location\":{\"directory\":\"/repo\"},"
        + "\"data\":{\"sessionID\":\"ses_9\",\"projectID\":\"prj_1\",\"location\":{\"directory\":\"/repo\"},"
        + "\"slug\":\"first\",\"title\":\"first\",\"agent\":\"build\",\"version\":\"1\"}}";

    /// <summary>A durable event at envelope version 2; the family carries both versions.</summary>
    public const string SessionDeletedEvent =
        "{\"id\":\"evt_2\",\"created\":2,\"type\":\"session.deleted\","
        + "\"durable\":{\"aggregateID\":\"ses_9\",\"seq\":2,\"version\":2},"
        + "\"location\":{\"directory\":\"/repo\"},"
        + "\"data\":{\"sessionID\":\"ses_9\"}}";

    /// <summary>The watermark the log emits once its replay reaches the captured sequence.</summary>
    public const string LogSyncedEvent =
        "{\"type\":\"log.synced\",\"aggregateID\":\"ses_9\",\"seq\":2}";

    /// <summary>A tag no generated variant owns; the carrier preserves it (ADR-0009).</summary>
    public const string UnknownLogEvent =
        "{\"id\":\"evt_3\",\"created\":3,\"type\":\"session.invented.tomorrow\","
        + "\"durable\":{\"aggregateID\":\"ses_9\",\"seq\":3,\"version\":1},\"data\":{}}";

    /// <summary>The cause a mid-stream failure frame carries under the declared failure event.</summary>
    public const string StreamFailureCause = "[{\"_tag\":\"Die\",\"defect\":\"boom\"}]";

    /// <summary>Frames one payload per event-stream frame, the shape the reader consumes.</summary>
    public static string Frames(params string[] payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);

        return string.Concat(payloads.Select(static payload => $"data: {payload}\n\n"));
    }

    /// <summary>Frames a payload under an explicit event name, which the wire writes only for a signal.</summary>
    public static string NamedFrame(string name, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(payload);

        return $"event: {name}\ndata: {payload}\n\n";
    }
}
