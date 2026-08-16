namespace OpenCode.Sdk.TestSupport;

internal sealed record RecordedRequest
{
    public required Uri? RequestUri { get; init; }

    public required HttpMethod Method { get; init; }

    /// <summary>Gets every request header, values joined with commas.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public string? Authorization { get; init; }

    public string? UserAgent { get; init; }

    public string? ContentType { get; init; }

    public string? Body { get; init; }
}
