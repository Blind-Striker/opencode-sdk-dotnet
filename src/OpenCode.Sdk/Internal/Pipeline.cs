using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OpenCode.Sdk.Internal;

/// <summary>Owns request decoration, sending, buffering, and error-channel policy for every operation.</summary>
internal sealed class Pipeline : IDisposable
{
    private const string EventStreamMediaType = "text/event-stream";

    private readonly AuthenticationHeaderValue? _authorization;
    private readonly string _endpointBase;
    private readonly HttpClient _httpClient;
    private readonly LocationSelector? _location;
    private readonly bool _ownsHttpClient;
    private readonly ProductInfoHeaderValue _userAgent;
    private bool _disposed;

    internal Pipeline(HttpClient httpClient, bool ownsHttpClient, IOpenCodeClientOptions options)
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
        _ownsHttpClient = ownsHttpClient;
        _endpointBase = EndpointPolicy.Normalize(endpoint);
        _location = options.Location;
        _userAgent = UserAgentPolicy.Resolve();

        // The options are read exactly once, here: the pipeline holds an immutable snapshot,
        // so mutating the options object after construction never changes a built client.
        _authorization = password is null
            ? null
            : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
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
        var httpClient = CreateOwnedHttpClient();
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
    public IAsyncEnumerable<TPayload> ExecuteStreamAsync<TPayload>(HttpMethod method, string route,
        IStreamAdapter<TPayload> adapter, CancellationToken cancellationToken)
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
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>The owned transport: pooled connection lifetime keeps DNS rotation alive on modern TFMs.</summary>
    private static HttpClient CreateOwnedHttpClient()
    {
#if NET
        SocketsHttpHandler? handler = null;
        try
        {
            handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
            var httpClient = new HttpClient(handler, disposeHandler: true);
            handler = null;
            return httpClient;
        }
        finally
        {
            handler?.Dispose();
        }
#else
        // net472/netstandard2.0 stay on the default handler.
        return new HttpClient();
#endif
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

        int status;
        string rawBody;
        using (var request = new HttpRequestMessage(method, new Uri(_endpointBase + route, UriKind.Absolute)))
        {
            if (body is not null)
            {
                request.Content = CreateJsonContent(body, bodyTypeInfo!);
            }

            Decorate(request);
            using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
            status = (int)response.StatusCode;
            rawBody = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var adapted = adapter.Adapt(status, rawBody);
        return adapted.IsError && errorBehavior is ErrorBehavior.Default
            ? throw CreateApiException(adapted)
            : adapted;
    }

    private async IAsyncEnumerable<TPayload> ExecuteStreamCoreAsync<TPayload>(HttpMethod method, string route,
        IStreamAdapter<TPayload> adapter, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_endpointBase + route, UriKind.Absolute));
        Decorate(request);
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
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
            var rawBody = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            throw OpenCodeErrorReader.CreateApiException(status, adapter.ReadError(status, rawBody), rawBody);
        }

        // A success that is not an event stream cannot be framed, so it fails as a
        // protocol error rather than being read as one frame of garbage.
        if (response.Content?.Headers.ContentType?.MediaType is not EventStreamMediaType)
        {
            throw new OpenCodeTransportException(
                "The opencode API answered a streaming operation without an event-stream body.");
        }

        // The response owns the body stream, so disposing it here would only duplicate the
        // disposal the enclosing using already performs when enumeration ends.
        var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var reader = new ServerSentEventReader();
        await foreach (var frame in reader.ReadAsync(body, cancellationToken).ConfigureAwait(false))
        {
            yield return ReadFramePayload(frame, adapter.PayloadTypeInfo);
        }
    }

    /// <summary>A frame the operation's contract cannot decode is a protocol failure, never an event.</summary>
    private static TPayload ReadFramePayload<TPayload>(string frame, JsonTypeInfo<TPayload> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(frame, typeInfo)
                   ?? throw new OpenCodeTransportException("The opencode event stream produced a null frame payload.");
        }
        catch (JsonException exception)
        {
            throw new OpenCodeTransportException("The opencode event stream produced a malformed frame payload.", exception);
        }
    }

    /// <summary>JSON is UTF-8 by definition (RFC 8259); the content type carries no charset.</summary>
    private static ByteArrayContent CreateJsonContent<TBody>(TBody body, JsonTypeInfo<TBody> bodyTypeInfo)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, bodyTypeInfo));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private void Decorate(HttpRequestMessage request)
    {
        if (_authorization is not null)
        {
            request.Headers.Authorization = _authorization;
        }

        // The ambient location rides the middleware's header channel, and the two members
        // travel differently: the server percent-decodes the directory header but reads the
        // workspace one verbatim, so the escaping mirrors that asymmetry exactly. Escaping
        // also keeps a non-ASCII path sendable, since header values cannot carry it raw. The
        // server resolves any explicit per-request location query first, so no client-side
        // merge exists.
        if (_location?.Directory is { } directory)
        {
            _ = request.Headers.TryAddWithoutValidation("x-opencode-directory", Uri.EscapeDataString(directory));
        }

        if (_location?.Workspace is { } workspace)
        {
            _ = request.Headers.TryAddWithoutValidation("x-opencode-workspace", workspace);
        }

        request.Headers.UserAgent.Add(_userAgent);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or ObjectDisposedException)
        {
            // ObjectDisposedException covers a dispose-during-send race; the pre-send disposed
            // guard stays outside this catch.
            throw new OpenCodeTransportException("The opencode server could not be reached.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller's token is untouched, so this cancellation is the transport timing
            // out; genuine caller cancellation passes through in the token-canceled case.
            throw new OpenCodeTransportException("The opencode server did not respond within the transport timeout.", exception);
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // InvalidOperationException covers an unusable response charset surfaced by
            // ReadAsStringAsync.
            throw new OpenCodeTransportException("The opencode response body could not be read.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller's token is untouched, so this cancellation is the transport timing
            // out mid-body; genuine caller cancellation passes through untouched.
            throw new OpenCodeTransportException("The opencode response body could not be read.", exception);
        }
    }

    private static OpenCodeApiException CreateApiException(OpenCodeResponse response) =>
        OpenCodeErrorReader.CreateApiException(response.Status, response.Error, response.RawBody);
}
