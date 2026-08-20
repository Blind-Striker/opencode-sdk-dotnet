using System.Net;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>Describes one raw HTTP response served by the loopback transport fixture.</summary>
internal sealed record LoopbackHttpResponse
{
    public required HttpStatusCode StatusCode { get; init; }

    public string Body { get; init; } = string.Empty;

    public string? ContentType { get; init; }

    public string? Location { get; init; }

    public bool KeepOpen { get; init; }
}
