using System.Net.Http.Headers;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Decorates every request with the construction-time header snapshot: authorization, the
/// ambient location, and the user agent. Knowledge source: upstream-observed — the location
/// headers mirror the middleware's decoding asymmetry, re-verified at every spec refresh. A
/// per-call <see cref="PipelineMessage.PerCallLocation"/> merges over that snapshot member by
/// member: a set member wins, an unset one inherits the ambient value, and there is no way to
/// clear an ambient member for one call. Both members ride the same header channel — this is
/// uniform injection, not the query-string per-request channel some operations declare;
/// session routes resolve location from the session and ignore these headers server-side, so
/// sending them there is a harmless no-op.
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
        Decorate(message.Request, message.PerCallLocation);
        await ProcessNextAsync(message, remaining).ConfigureAwait(false);
    }

    private void Decorate(HttpRequestMessage request, LocationSelector? perCall)
    {
        if (_authorization is not null)
        {
            request.Headers.Authorization = _authorization;
        }

        var escapedDirectory = perCall?.Directory is { } directory
            ? Uri.EscapeDataString(directory)
            : _escapedDirectory;
        var workspace = perCall?.Workspace ?? _workspace;

        if (escapedDirectory is not null)
        {
            _ = request.Headers.TryAddWithoutValidation("x-opencode-directory", escapedDirectory);
        }

        if (workspace is not null)
        {
            _ = request.Headers.TryAddWithoutValidation("x-opencode-workspace", workspace);
        }

        request.Headers.UserAgent.Add(_userAgent);
    }
}
