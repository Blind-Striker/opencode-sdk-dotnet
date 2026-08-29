using System.Net.Http.Headers;
using BenchmarkDotNet.Attributes;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Performance.Tests.Support;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// Route and query composition alone, per request shape: the constant route as the
/// composition-free control, the two-parameter path route, and the query-bearing list route,
/// plus the path route decorated through a per-call location merge over an ambient snapshot —
/// the member-by-member merge <see cref="RequestDecorationPolicy"/> performs on every request
/// that carries a per-call <see cref="OpenCodeRequestOptions.Location"/>. Research doc 20 D1
/// gates any composition rework on these numbers being visible next to materialization.
/// </summary>
[MemoryDiagnoser]
public class RouteCompositionBenchmarks
{
    private const string EndpointBase = "https://benchmark.invalid";
    private const string SessionId = "ses_bench0000000000000000001";
    private const string MessageId = "msg_bench0000000000000000001";

    private static readonly MessageListRequest FullQuery = new()
    {
        Limit = "50",
        Order = ListOrder.Descending,
        Cursor = "cur_0123456789abcdef",
    };

    /// <summary>
    /// The ambient snapshot a client is constructed with. <see cref="PerCallLocation"/> overrides
    /// its directory and leaves its workspace unset, so the merge below exercises both member
    /// rules at once: a set member wins (directory, forcing the escape to recompute) and an unset
    /// member inherits the ambient value (workspace).
    /// </summary>
    private static readonly LocationSelector AmbientLocation = new() { Directory = "/repo", Workspace = "wrk_bench" };

    private static readonly LocationSelector PerCallLocation = new() { Directory = "/repo/worktree" };
    private static readonly ReadOnlyMemory<PipelinePolicy> Terminal = new PipelinePolicy[] { NoOpTerminalPolicy.Instance };

    private static readonly RequestDecorationPolicy Decoration = new(
        new AuthenticationHeaderValue("Basic", "YmVuY2g6YmVuY2g="),
        AmbientLocation,
        UserAgentPolicy.Resolve());

    /// <summary>The constant no-parameter route.</summary>
    [Benchmark]
    public string ConstantRoute() => OpenCodeRoutes.Health.Get;

    /// <summary>The two-parameter path route: segment concatenation plus value escaping.</summary>
    [Benchmark]
    public string PathRoute() => OpenCodeRoutes.Sessions.GetMessage(SessionId, MessageId);

    /// <summary>The query-bearing list route: builder, escaping, and final concatenation.</summary>
    [Benchmark]
    public string QueryRoute() =>
        OpenCodeRoutes.Sessions.ListMessages(SessionId, FullQuery);

    /// <summary>
    /// <see cref="PathRoute"/>'s composed route, decorated through the same
    /// <see cref="RequestDecorationPolicy"/> every request rides, with a per-call location that
    /// merges over the ambient snapshot member by member (directory overridden and re-escaped,
    /// workspace inherited). Reads against <see cref="PathRoute"/> to isolate the merge's added
    /// cost from composition alone.
    /// </summary>
    [Benchmark]
    public async Task<string> PathRouteWithLocationMergeAsync()
    {
        var route = OpenCodeRoutes.Sessions.GetMessage(SessionId, MessageId);
        using var message = new PipelineMessage
        {
            Request = new HttpRequestMessage(HttpMethod.Get, new Uri(EndpointBase + route, UriKind.Absolute)),
            PerCallLocation = PerCallLocation,
        };

        await Decoration.ProcessAsync(message, Terminal).ConfigureAwait(false);
        return message.Request.Headers.GetValues("x-opencode-directory").First();
    }
}
