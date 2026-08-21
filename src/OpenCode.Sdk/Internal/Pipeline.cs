using System.Diagnostics;
using System.Globalization;
#if !NET
using System.Net;
#endif
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OpenCode.Sdk.Internal;

/// <summary>Owns request decoration, sending, buffering, and error-channel policy for every operation.</summary>
internal sealed class Pipeline : IDisposable
{
    private const int ConnectionLifetimeMilliseconds = 120_000;
    private const string EventStreamMediaType = "text/event-stream";

    private readonly AuthenticationHeaderValue? _authorization;
    private readonly string _endpointBase;
    private readonly HttpClient _httpClient;
    private readonly LocationSelector? _location;
    private readonly bool _ownsHttpClient;
    private readonly ResponseBodyReader _responseBodyReader = new();
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
        var httpClient = CreateOwnedHttpClient(options.Endpoint);
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
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>Builds the non-redirecting owned transport with endpoint rotation on every runtime.</summary>
    private static HttpClient CreateOwnedHttpClient(Uri endpoint)
    {
        HttpMessageHandler? handler = null;
        try
        {
            handler = CreateOwnedHttpHandler(endpoint);
            var httpClient = new HttpClient(handler, disposeHandler: true);
            handler = null;
            return httpClient;
        }
        finally
        {
            handler?.Dispose();
        }
    }

    /// <summary>Creates the real platform handler; internal so tests can observe its sealed policy.</summary>
    internal static HttpMessageHandler CreateOwnedHttpHandler(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
#if NET
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMilliseconds(ConnectionLifetimeMilliseconds),
        };
#else
        ConfigureDownlevelServicePoint(endpoint);
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
        };
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

        TResponse adapted;
        using (var request = new HttpRequestMessage(method, new Uri(_endpointBase + route, UriKind.Absolute)))
        {
            if (body is not null)
            {
                request.Content = CreateJsonContent(body, bodyTypeInfo!);
            }

            Decorate(request);
            var requestStarted = Stopwatch.GetTimestamp();
            using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
            var remainingTimeout = GetRemainingTimeout(requestStarted);
            var status = (int)response.StatusCode;
            RefuseRedirectStatus(status);
            if (status == adapter.SuccessStatusCode)
            {
                if (!adapter.ReadsSuccessBody)
                {
                    adapted = adapter.AdaptSuccess(status, []);
                }
                else
                {
                    var encodedBody = await _responseBodyReader.ReadAsync(response, remainingTimeout, cancellationToken).ConfigureAwait(false);
                    adapted = encodedBody.DecodedBody is { } decoded
                        ? adapter.Adapt(status, decoded)
                        : adapter.AdaptSuccess(status, encodedBody.Utf8Body.Span);
                }
            }
            else
            {
                var rawBody = (await _responseBodyReader.ReadAsync(response, remainingTimeout, cancellationToken).ConfigureAwait(false))
                    .GetDecodedBody();
                adapted = adapter.Adapt(status, rawBody);
            }
        }

        return adapted.IsError && errorBehavior is ErrorBehavior.Default
            ? throw CreateApiException(adapted)
            : adapted;
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
#if !NET
        if (_ownsHttpClient)
        {
            ConfigureDownlevelServicePoint(request.RequestUri!);
        }
#endif
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

    private static OpenCodeApiException CreateApiException(OpenCodeResponse response) =>
        OpenCodeErrorReader.CreateApiException(response.Status, response.Error, response.RawBody);

    private TimeSpan GetRemainingTimeout(long requestStarted)
    {
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan)
        {
            return Timeout.InfiniteTimeSpan;
        }

        var elapsedSeconds = (Stopwatch.GetTimestamp() - requestStarted) / (double)Stopwatch.Frequency;
        var remaining = _httpClient.Timeout - TimeSpan.FromSeconds(elapsedSeconds);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

#if !NET
    private static void ConfigureDownlevelServicePoint(Uri endpoint)
    {
        var servicePoint = ServicePointManager.FindServicePoint(endpoint, WebRequest.DefaultWebProxy);
        servicePoint.ConnectionLimit = int.MaxValue;
        servicePoint.ConnectionLeaseTimeout = ConnectionLifetimeMilliseconds;
    }
#endif

    private static void RefuseRedirectStatus(int status)
    {
        if (status is >= 300 and < 400)
        {
            throw new OpenCodeTransportException($"The opencode API returned undeclared redirect status {status.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private async IAsyncEnumerable<TPayload> ExecuteStreamCoreAsync<TPayload, TCause>(HttpMethod method, string route,
        IStreamAdapter<TPayload, TCause> adapter, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TCause : IReadOnlyList<Models.IOpenCodeStreamFailureCause>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var request = new HttpRequestMessage(method, new Uri(_endpointBase + route, UriKind.Absolute));
        Decorate(request);
        var requestStarted = Stopwatch.GetTimestamp();
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        RefuseRedirectStatus(status);

        // Any other 2xx is outside the declared contract: a protocol failure, never an API
        // error — the same reading the one-shot adapters give it.
        if (status is > 200 and < 300)
        {
            throw new OpenCodeTransportException(
                $"The opencode API returned undeclared success status {status.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (status is not 200)
        {
            var rawBody = (await _responseBodyReader
                    .ReadAsync(response, GetRemainingTimeout(requestStarted), cancellationToken)
                    .ConfigureAwait(false))
                .GetDecodedBody();
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
        // disposal the enclosing using already performs when enumeration ends.
        var body = await ReadBodyStreamAsync(response, cancellationToken).ConfigureAwait(false);
        var frames = new ServerSentEventReader()
            .ReadAsync(body, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
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
            catch (Exception exception)
                when (cancellationToken.IsCancellationRequested
                      && exception is HttpRequestException or IOException or ObjectDisposedException)
            {
                throw new OperationCanceledException("The opencode event stream read was canceled.", exception, cancellationToken);
            }
            catch (Exception exception)
                when (exception is HttpRequestException or IOException or ObjectDisposedException)
            {
                throw new OpenCodeTransportException("The opencode event stream could not be read.", exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new OpenCodeTransportException("The opencode event stream could not be read.", exception);
            }

            if (!moved)
            {
                break;
            }

            yield return ReadStreamPayload(frames.Current, adapter);
        }
    }

    private static async Task<Stream> ReadBodyStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (cancellationToken.IsCancellationRequested
                  && exception is HttpRequestException or IOException or ObjectDisposedException)
        {
            throw new OperationCanceledException("The opencode event stream read was canceled.", exception, cancellationToken);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or ObjectDisposedException)
        {
            throw new OpenCodeTransportException("The opencode event stream could not be read.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OpenCodeTransportException("The opencode event stream could not be read.", exception);
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

    /// <summary>
    /// Reads one dispatched frame: the contract names a failure frame and leaves every other
    /// name undeclared, so only an unnamed frame carries a payload.
    /// </summary>
    private static TPayload ReadStreamPayload<TPayload, TCause>(ServerSentEvent frame, IStreamAdapter<TPayload, TCause> adapter)
        where TCause : IReadOnlyList<Models.IOpenCodeStreamFailureCause>
    {
        if (string.Equals(frame.Name, adapter.FailureEventName, StringComparison.Ordinal))
        {
            throw new OpenCodeStreamFailureException(ReadStreamCause(frame.Data, adapter.CauseTypeInfo));
        }

        if (!string.Equals(frame.Name, ServerSentEvent.DefaultName, StringComparison.Ordinal))
        {
            throw new OpenCodeTransportException($"The opencode event stream produced an undeclared frame named '{frame.Name}'.");
        }

        return ReadFramePayload(frame.Data, adapter.PayloadTypeInfo);
    }

    private static TCause ReadStreamCause<TCause>(string frame, JsonTypeInfo<TCause> typeInfo)
        where TCause : IReadOnlyList<Models.IOpenCodeStreamFailureCause>
    {
        try
        {
            return JsonSerializer.Deserialize(frame, typeInfo)
                   ?? throw new OpenCodeTransportException("The opencode event stream produced a null failure cause.");
        }
        catch (JsonException exception)
        {
            throw new OpenCodeTransportException("The opencode event stream produced an unmaterializable failure cause.", exception);
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
}
