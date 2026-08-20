using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Internal;

/// <summary>Maps one HTTP response onto an operation's typed envelope.</summary>
/// <typeparam name="TResponse">The operation's response envelope type.</typeparam>
internal abstract class ResponseAdapter<TResponse>
    where TResponse : OpenCodeResponse
{
    /// <summary>Gets the operation's single declared success status.</summary>
    public abstract int SuccessStatusCode { get; }

    /// <summary>Gets whether the declared success status carries a JSON body.</summary>
    public virtual bool ReadsSuccessBody => true;

    /// <summary>Maps a validated UTF-8 success body onto the typed envelope.</summary>
    public abstract TResponse AdaptSuccess(int status, ReadOnlySpan<byte> utf8Body);

    /// <summary>Maps a buffered response body onto the typed envelope.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="rawBody">The buffered response body.</param>
    /// <returns>The typed envelope; error responses set <see cref="OpenCodeResponse.IsError"/>.</returns>
    public abstract TResponse Adapt(int status, string rawBody);

    /// <summary>Reads a bare success payload; a malformed body is a protocol failure.</summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="rawBody">The buffered success body.</param>
    /// <param name="typeInfo">The source-generated payload metadata.</param>
    /// <returns>The payload.</returns>
    protected static TPayload ReadBarePayload<TPayload>(string rawBody, JsonTypeInfo<TPayload> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        ArgumentNullException.ThrowIfNull(typeInfo);

        try
        {
            return JsonSerializer.Deserialize(rawBody, typeInfo)
                   ?? throw new OpenCodeTransportException("The opencode API returned a null success body.");
        }
        catch (JsonException exception)
        {
            throw new OpenCodeTransportException("The opencode API returned a malformed success body.", exception);
        }
    }

    /// <summary>Reads a bare UTF-8 success payload; a malformed body is a protocol failure.</summary>
    protected static TPayload ReadBarePayload<TPayload>(ReadOnlySpan<byte> utf8Body, JsonTypeInfo<TPayload> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        try
        {
            return JsonSerializer.Deserialize(utf8Body, typeInfo)
                   ?? throw new OpenCodeTransportException("The opencode API returned a null success body.");
        }
        catch (JsonException exception)
        {
            throw new OpenCodeTransportException("The opencode API returned a malformed success body.", exception);
        }
    }

    /// <summary>Builds the protocol failure for a success status the operation does not declare.</summary>
    /// <param name="status">The undeclared 2xx status code.</param>
    /// <returns>The transport failure the adapter throws.</returns>
    protected static OpenCodeTransportException UndeclaredSuccessFailure(int status) =>
        new($"The opencode API returned undeclared success status {status.ToString(CultureInfo.InvariantCulture)}.");

    /// <summary>
    /// Reads a typed error tolerantly: malformed JSON yields <see langword="null"/> so the raw
    /// body remains the only record; an unknown tag keeps its carrier; a known tag outside the
    /// operation's status map — or on an undeclared status, when <paramref name="allowedTags"/>
    /// is <see langword="null"/> — downgrades to the unknown carrier so the operation contract
    /// never widens.
    /// </summary>
    /// <param name="rawBody">The buffered error body.</param>
    /// <param name="allowedTags">The tags the status map declares for this status, or <see langword="null"/> for an undeclared status.</param>
    /// <returns>The typed error, or <see langword="null"/> when the body could not be parsed.</returns>
    protected static IOpenCodeError? ReadTolerantError(string rawBody, IReadOnlyCollection<string>? allowedTags) =>
        OpenCodeErrorReader.Read(rawBody, allowedTags);
}
