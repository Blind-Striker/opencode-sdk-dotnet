using OpenCode.Sdk.Models;

namespace OpenCode.Sdk;

/// <summary>Carries the shared metadata of every typed opencode response envelope.</summary>
public abstract record OpenCodeResponse
{
    /// <summary>Gets the HTTP status code of the response.</summary>
    public required int Status { get; init; }

    /// <summary>Gets a value indicating whether the response is an API error.</summary>
    public bool IsError { get; init; }

    /// <summary>Gets the typed API error, or <see langword="null"/> when the error body could not be parsed.</summary>
    public OpenCodeError? Error { get; init; }

    /// <summary>Gets the raw error body; populated only on the error path.</summary>
    public string? RawBody { get; init; }
}
