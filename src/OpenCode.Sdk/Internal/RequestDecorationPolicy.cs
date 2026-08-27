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
/// sending them there is a harmless no-op. The message may also carry
/// <see cref="PipelineMessage.DeclaredHeaders"/> — headers the pinned document declares as
/// parameters of one operation. Those are applied uniformly, entry by entry: this policy never
/// learns which family or header name it is writing, so no operation's knowledge leaks here.
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
        // non-ASCII path sendable, since header values cannot carry it raw. This snapshot is
        // header-only and never merges with an operation's own query-string location channel —
        // those remain unrelated. The client-side merge this class performs (Decorate, below)
        // is entirely within the header channel, resolving a per-call PerCallLocation over this
        // snapshot member by member.
        _escapedDirectory = location?.Directory is { } directory ? Uri.EscapeDataString(directory) : null;
        _workspace = location?.Workspace;
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, ReadOnlyMemory<PipelinePolicy> remaining)
    {
        Decorate(message.Request, message.PerCallLocation, message.DeclaredHeaders);
        await ProcessNextAsync(message, remaining).ConfigureAwait(false);
    }

    private void Decorate(HttpRequestMessage request, LocationSelector? perCall, IReadOnlyList<DeclaredHeader>? declaredHeaders)
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

        // The document already fixed each name and the raw method already dropped the omitted
        // ones, so the values ride unvalidated exactly as the location headers do.
        if (declaredHeaders is not null)
        {
            for (var index = 0; index < declaredHeaders.Count; index++)
            {
                var header = declaredHeaders[index];
                _ = request.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
        }

        request.Headers.UserAgent.Add(_userAgent);
    }
}
