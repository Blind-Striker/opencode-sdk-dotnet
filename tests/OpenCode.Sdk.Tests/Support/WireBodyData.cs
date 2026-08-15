namespace OpenCode.Sdk.Tests.Support;

/// <summary>Canned wire bodies and envelope shapes for the contract tests.</summary>
internal static class WireBodyData
{
    public const string HealthOk = "{\"healthy\":true,\"version\":\"0.0.0-test\",\"pid\":42}";

    public const string UnauthorizedError = "{\"_tag\":\"UnauthorizedError\",\"message\":\"password required\"}";

    public const string SessionNotFoundError = "{\"_tag\":\"SessionNotFoundError\",\"sessionID\":\"ses_9\",\"message\":\"gone\"}";

    public const string MessageNotFoundError = "{\"_tag\":\"MessageNotFoundError\",\"sessionID\":\"ses_9\",\"messageID\":\"msg_1\",\"message\":\"gone\"}";

    public const string UnknownError = "{\"_tag\":\"UnknownError\",\"message\":\"boom\"}";

    public static string Envelope(string datum)
    {
        ArgumentNullException.ThrowIfNull(datum);

        return $"{{\"data\":{datum}}}";
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
}
