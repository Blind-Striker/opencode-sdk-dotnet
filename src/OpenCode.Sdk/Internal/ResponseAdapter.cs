namespace OpenCode.Sdk.Internal;

/// <summary>Maps one buffered HTTP response onto an operation's typed envelope.</summary>
/// <typeparam name="TResponse">The operation's response envelope type.</typeparam>
internal abstract class ResponseAdapter<TResponse>
    where TResponse : OpenCodeResponse
{
    /// <summary>Maps a buffered response body onto the typed envelope.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="rawBody">The buffered response body.</param>
    /// <returns>The typed envelope; error responses set <see cref="OpenCodeResponse.IsError"/>.</returns>
    public abstract TResponse Adapt(int status, string rawBody);
}
