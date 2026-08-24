using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// The composed request pipeline behind every operation: an immutable policy roster —
/// decoration, buffering, transport — carries each message, and the materializer maps the
/// buffered result onto the operation's envelope. Construction owns option validation and
/// decides transport ownership; the planes are the narrative behind the generated-facing
/// entry points.
/// </summary>
internal sealed class Pipeline : IDisposable
{
    private const string EventStreamMediaType = "text/event-stream";

    /// <summary>
    /// JSON is UTF-8 by definition (RFC 8259); the content type carries no charset. The
    /// instance is shared across requests and <see cref="MediaTypeHeaderValue"/> is mutable,
    /// so no code may ever write to it.
    /// </summary>
    private static readonly MediaTypeHeaderValue JsonMediaType = new("application/json");

    private readonly string _endpointBase;
    private readonly IEventStreamFramer _framer;

    /// <summary>Read only for the per-request budget snapshot; its lifetime belongs to <see cref="_transport"/>.</summary>
    private readonly HttpClient _httpClient;

    private readonly ResponseMaterializer _materializer = new();
    private readonly PipelinePolicy[] _policies;
    private readonly TransportPolicy _transport;
    private bool _disposed;

    internal Pipeline(HttpClient httpClient, bool ownsHttpClient, IOpenCodeClientOptions options,
        IEventStreamFramer? framer = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        var endpoint = options.Endpoint ?? throw new ArgumentException("OpenCodeClientOptions.Endpoint is required.", nameof(options));

        // Basic credentials ride the wire as "username:password", so a blank name or a colon
        // inside it would corrupt the header instead of failing a login.
        var username = options.Username;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("The username cannot be empty or whitespace.", nameof(options));
        }

        if (username.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("The username cannot contain a colon.", nameof(options));
        }

        // An explicitly blank password has no upstream meaning (a server without configured
        // authentication expects no credentials at all), so it fails loudly; null is the
        // anonymous spelling.
        var password = options.Password;
        if (password is not null && string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException(
                "An explicit password cannot be empty or whitespace; leave it null for a server without authentication.",
                nameof(options));
        }

        _httpClient = httpClient;
        _endpointBase = EndpointPolicy.Normalize(endpoint);
        _framer = framer ?? new ServerSentEventFramer();

        // The options are read exactly once, here: the policies hold an immutable snapshot,
        // so mutating the options object after construction never changes a built client.
        var authorization = password is null
            ? null
            : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        _transport = new TransportPolicy(httpClient, ownsHttpClient);
        _policies =
        [
            new RequestDecorationPolicy(authorization, options.Location, UserAgentPolicy.Resolve()),
            new ResponseBufferingPolicy(),
            _transport,
        ];
    }

    public static Pipeline Create(OpenCodeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Endpoint is null)
        {
            throw new ArgumentException("OpenCodeClientOptions.Endpoint is required.", nameof(options));
        }

        // Validate before constructing the owned client so a refused endpoint leaks nothing.
        _ = EndpointPolicy.Normalize(options.Endpoint);
        var httpClient = TransportPolicy.CreateOwnedHttpClient(options.Endpoint);
        try
        {
            return new Pipeline(httpClient, ownsHttpClient: true, options);
        }
        catch
        {
            // Credential validation still throws inside the constructor; the owned
            // transport must not outlive a refused construction.
            httpClient.Dispose();
            throw;
        }
    }

    public static Pipeline Create(HttpClient httpClient, OpenCodeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Endpoint is null)
        {
            throw new ArgumentException("A caller-supplied HttpClient requires OpenCodeClientOptions.Endpoint.", nameof(options));
        }

        return new Pipeline(httpClient, ownsHttpClient: false, options);
    }

    public Task<TResponse> ExecuteAsync<TResponse>(HttpMethod method, string route, ResponseAdapter<TResponse> adapter,
        OpenCodeRequestOptions? options, CancellationToken cancellationToken)
        where TResponse : OpenCodeResponse =>
        ExecuteCoreAsync<object, TResponse>(method, route, body: null, bodyTypeInfo: null, adapter, options, cancellationToken);

    public Task<TResponse> ExecuteAsync<TBody, TResponse>(HttpMethod method, string route, TBody body,
        JsonTypeInfo<TBody> bodyTypeInfo, ResponseAdapter<TResponse> adapter, OpenCodeRequestOptions? options,
        CancellationToken cancellationToken)
        where TBody : class
        where TResponse : OpenCodeResponse
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(bodyTypeInfo);
        return ExecuteCoreAsync(method, route, body, bodyTypeInfo, adapter, options, cancellationToken);
    }

    /// <summary>
    /// Opens a streaming operation: the status and content-type walls answer before the
    /// body is read, then each event frame's payload is yielded as it arrives. The
    /// one-shot buffer is never involved, and a stream always throws on an error status —
    /// it has no envelope for one to ride.
    /// </summary>
    public IAsyncEnumerable<TPayload> ExecuteStreamAsync<TPayload, TCause>(HttpMethod method, string route,
        IStreamAdapter<TPayload, TCause> adapter, CancellationToken cancellationToken)
        where TCause : IReadOnlyList<Models.IOpenCodeStreamFailureCause>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(adapter);

        if (route[0] is not '/')
        {
            throw new ArgumentException("Routes must start with '/'.", nameof(route));
        }

        return ExecuteStreamCoreAsync(method, route, adapter, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport.Dispose();
    }

    private async Task<TResponse> ExecuteCoreAsync<TBody, TResponse>(HttpMethod method, string route, TBody? body,
        JsonTypeInfo<TBody>? bodyTypeInfo, ResponseAdapter<TResponse> adapter, OpenCodeRequestOptions? options,
        CancellationToken cancellationToken)
        where TBody : class
        where TResponse : OpenCodeResponse
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(adapter);

        if (route[0] is not '/')
        {
            throw new ArgumentException("Routes must start with '/'.", nameof(route));
        }

        // Refused before sending: an undefined value must never silently pick an error channel.
        var errorBehavior = options?.ErrorBehavior ?? ErrorBehavior.Default;
        if (errorBehavior is not (ErrorBehavior.Default or ErrorBehavior.NoThrow))
        {
            throw new ArgumentOutOfRangeException(nameof(options), errorBehavior, "Unknown ErrorBehavior value.");
        }

        TResponse adapted;
        using (var message = CreateMessage(method, route, body, bodyTypeInfo, adapter, cancellationToken))
        {
            await SendThroughPoliciesAsync(message).ConfigureAwait(false);
            adapted = _materializer.Materialize(message, adapter);
        }

        return adapted.IsError && errorBehavior is ErrorBehavior.Default
            ? throw CreateApiException(adapted)
            : adapted;
    }

    private async IAsyncEnumerable<TPayload> ExecuteStreamCoreAsync<TPayload, TCause>(HttpMethod method, string route,
        IStreamAdapter<TPayload, TCause> adapter, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TCause : IReadOnlyList<Models.IOpenCodeStreamFailureCause>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var message = CreateStreamMessage(method, route, cancellationToken);
        await SendThroughPoliciesAsync(message).ConfigureAwait(false);
        var response = message.Response!;
        var status = (int)response.StatusCode;

        // Any other 2xx is outside the declared contract: a protocol failure, never an API
        // error — the same reading the one-shot adapters give it.
        if (status is > 200 and < 300)
        {
            throw new OpenCodeTransportException(
                $"The opencode API returned undeclared success status {status.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (status is not 200)
        {
            var rawBody = _materializer.ReadErrorBody(message);
            throw OpenCodeErrorReader.CreateApiException(status, adapter.ReadError(status, rawBody), rawBody);
        }

        // A success that is not an event stream cannot be framed, so it fails as a
        // protocol error rather than being read as one frame of garbage. Media types are
        // case-insensitive, so a proxy that rewrites the casing still matches.
        if (!string.Equals(response.Content?.Headers.ContentType?.MediaType, EventStreamMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenCodeTransportException("The opencode API answered a streaming operation without an event-stream body.");
        }

#if !NET
        using var cancellationRegistration = RegisterStreamCancellation(response, cancellationToken);
#endif

        // The response owns the body stream, so disposing it here would only duplicate the
        // disposal the message performs when enumeration ends.
        var body = await ReadBodyStreamAsync(response, cancellationToken).ConfigureAwait(false);
        var frames = _framer.ReadAsync(body, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var enumeration = frames.ConfigureAwait(false);
        while (true)
        {
            bool moved;

            // A yield cannot sit inside a try with a catch, so the read is guarded on its
            // own: a connection dying mid-stream is a transport failure here exactly as it
            // is on the one-shot path.
            try
            {
                moved = await frames.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (FailureClassification.Handles(exception, FailurePhase.EventStreamRead))
            {
                throw FailureClassification.Map(exception, FailurePhase.EventStreamRead, cancellationToken);
            }

            if (!moved)
            {
                break;
            }

            yield return FrameDispatch.ReadPayload(frames.Current, adapter);
        }
    }

    private ValueTask SendThroughPoliciesAsync(PipelineMessage message) =>
        _policies[0].ProcessAsync(message, _policies.AsMemory(1));

    private PipelineMessage CreateMessage<TBody, TResponse>(HttpMethod method, string route, TBody? body,
        JsonTypeInfo<TBody>? bodyTypeInfo, ResponseAdapter<TResponse> adapter, CancellationToken cancellationToken)
        where TBody : class
        where TResponse : OpenCodeResponse
    {
        var request = new HttpRequestMessage(method, new Uri(_endpointBase + route, UriKind.Absolute));
        try
        {
            if (body is not null)
            {
                request.Content = CreateJsonContent(body, bodyTypeInfo!);
            }

            return new PipelineMessage
            {
                Request = request,
                CancellationToken = cancellationToken,
                NetworkTimeout = _httpClient.Timeout,
                NoBodySuccessStatus = adapter.ReadsSuccessBody ? null : adapter.SuccessStatusCode,
            };
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private PipelineMessage CreateStreamMessage(HttpMethod method, string route, CancellationToken cancellationToken) =>
        new()
        {
            Request = new HttpRequestMessage(method, new Uri(_endpointBase + route, UriKind.Absolute)),
            CancellationToken = cancellationToken,
            NetworkTimeout = _httpClient.Timeout,
            BufferBody = false,
        };

    private static ByteArrayContent CreateJsonContent<TBody>(TBody body, JsonTypeInfo<TBody> bodyTypeInfo)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, bodyTypeInfo));
        content.Headers.ContentType = JsonMediaType;
        return content;
    }

    private static OpenCodeApiException CreateApiException(OpenCodeResponse response) =>
        OpenCodeErrorReader.CreateApiException(response.Status, response.Error, response.RawBody);

    private static async Task<Stream> ReadBodyStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (FailureClassification.Handles(exception, FailurePhase.EventStreamRead))
        {
            throw FailureClassification.Map(exception, FailurePhase.EventStreamRead, cancellationToken);
        }
    }

#if !NET
    private static CancellationTokenRegistration RegisterStreamCancellation(
        HttpResponseMessage response, CancellationToken cancellationToken) =>
        cancellationToken.Register(
            static state => ((HttpResponseMessage)state).Dispose(),
            response,
            useSynchronizationContext: false);
#endif
}
