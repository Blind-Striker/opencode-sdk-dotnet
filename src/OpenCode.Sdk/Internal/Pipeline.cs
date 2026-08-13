using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenCode.Sdk.Internal.Abstractions;

namespace OpenCode.Sdk.Internal;

/// <summary>Owns request decoration, sending, buffering, and error-channel policy for every operation.</summary>
internal sealed class Pipeline : IDisposable
{
    private const string BasicUser = "opencode";
    private const string PasswordVariable = "OPENCODE_SERVER_PASSWORD";

    private readonly AuthenticationHeaderValue? _authorization;
    private readonly string _endpointBase;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ProductInfoHeaderValue _userAgent;
    private bool _disposed;

    internal Pipeline(HttpClient httpClient, bool ownsHttpClient, Uri endpoint, string? password, IEnvironmentProvider environment)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(environment);

        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _endpointBase = EndpointPolicy.Normalize(endpoint);
        _userAgent = UserAgentPolicy.Resolve();

        // The environment fallback is read exactly once, here; requests reuse the resolved header.
        var resolvedPassword = string.IsNullOrEmpty(password) ? environment.GetEnvironmentVariable(PasswordVariable) : password;
        _authorization = string.IsNullOrEmpty(resolvedPassword)
            ? null
            : new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{BasicUser}:{resolvedPassword}")));
    }

    public static Pipeline Create(Uri endpoint, OpenCodeClientOptions? options, IEnvironmentProvider environment)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(environment);
        if (options?.Endpoint is not null)
        {
            throw new ArgumentException(
                "The endpoint constructor owns endpoint authority; leave OpenCodeClientOptions.Endpoint unset.",
                nameof(options));
        }

        // Validate before constructing the owned client so a refused endpoint leaks nothing.
        _ = EndpointPolicy.Normalize(endpoint);
        return new Pipeline(new HttpClient(), ownsHttpClient: true, endpoint, options?.Password, environment);
    }

    public static Pipeline Create(HttpClient httpClient, OpenCodeClientOptions options, IEnvironmentProvider environment)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        if (options.Endpoint is null)
        {
            throw new ArgumentException(
                "A caller-supplied HttpClient requires OpenCodeClientOptions.Endpoint.",
                nameof(options));
        }

        return new Pipeline(httpClient, ownsHttpClient: false, options.Endpoint, options.Password, environment);
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

    /// <summary>JSON is UTF-8 by definition (RFC 8259); the content type carries no charset.</summary>
    private static ByteArrayContent CreateJsonContent<TBody>(TBody body, JsonTypeInfo<TBody> bodyTypeInfo)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, bodyTypeInfo));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
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

    private void Decorate(HttpRequestMessage request)
    {
        if (_authorization is not null)
        {
            request.Headers.Authorization = _authorization;
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
            // guard stays outside this catch. OperationCanceledException passes through untouched.
            throw new OpenCodeTransportException("The opencode server could not be reached.", exception);
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
        catch (Exception exception) when (exception is HttpRequestException or IOException or ObjectDisposedException
                                              or InvalidOperationException)
        {
            // InvalidOperationException covers an unusable response charset surfaced by
            // ReadAsStringAsync. OperationCanceledException deliberately passes through untouched.
            throw new OpenCodeTransportException("The opencode response body could not be read.", exception);
        }
    }

    private static OpenCodeApiException CreateApiException(OpenCodeResponse response)
    {
        var status = response.Status.ToString(CultureInfo.InvariantCulture);
        var message = response.Error is null
            ? $"The opencode API returned status {status}."
            : $"The opencode API returned status {status} ('{response.Error.Tag}').";
        return new OpenCodeApiException(message, response.Status, response.Error, response.RawBody);
    }
}
