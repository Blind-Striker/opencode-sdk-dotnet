using System.Net.Http.Headers;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Decorates every request with the construction-time header snapshot: authorization, the
/// ambient location, and the user agent. Knowledge source: upstream-observed — the location
/// headers mirror the middleware's decoding asymmetry, re-verified at every spec refresh.
/// </summary>
internal sealed class RequestDecorationPolicy : PipelinePolicy
{
    private readonly AuthenticationHeaderValue? _authorization;
    private readonly string? _escapedDirectory;
    private readonly ProductInfoHeaderValue _userAgent;
    private readonly string? _workspace;

    public RequestDecorationPolicy(AuthenticationHeaderValue? authorization, LocationSelector? location, ProductInfoHeaderValue userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        _authorization = authorization;
        _userAgent = userAgent;

        // The ambient location rides the middleware's header channel, and the two members
        // travel differently: the server percent-decodes the directory header but reads the
        // workspace one verbatim, so the escaping mirrors that asymmetry exactly — computed
        // once, because the snapshot never changes after construction. Escaping also keeps a
        // non-ASCII path sendable, since header values cannot carry it raw. The server
        // resolves any explicit per-request location query first, so no client-side merge
        // exists.
        _escapedDirectory = location?.Directory is { } directory ? Uri.EscapeDataString(directory) : null;
        _workspace = location?.Workspace;
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, ReadOnlyMemory<PipelinePolicy> remaining)
    {
        Decorate(message.Request);
        await ProcessNextAsync(message, remaining).ConfigureAwait(false);
    }

    private void Decorate(HttpRequestMessage request)
    {
        if (_authorization is not null)
        {
            request.Headers.Authorization = _authorization;
        }

        if (_escapedDirectory is not null)
        {
            _ = request.Headers.TryAddWithoutValidation("x-opencode-directory", _escapedDirectory);
        }

        if (_workspace is not null)
        {
            _ = request.Headers.TryAddWithoutValidation("x-opencode-workspace", _workspace);
        }

        request.Headers.UserAgent.Add(_userAgent);
    }
}
