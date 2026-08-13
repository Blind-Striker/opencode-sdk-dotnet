namespace OpenCode.Sdk.Tests.Support;

internal sealed record RecordedRequest
{
    public required Uri? RequestUri { get; init; }

    public required HttpMethod Method { get; init; }

    public string? Authorization { get; init; }

    public string? UserAgent { get; init; }

    public string? ContentType { get; init; }

    public string? Body { get; init; }
}
