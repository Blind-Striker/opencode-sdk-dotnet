using BenchmarkDotNet.Attributes;

namespace OpenCode.Sdk.Performance.Tests.Benchmarks;

/// <summary>
/// Route and query composition alone, per request shape: the constant route as the
/// composition-free control, the two-parameter path route, and the query-bearing list route.
/// Research doc 20 D1 gates any composition rework on these numbers being visible next to
/// materialization.
/// </summary>
[MemoryDiagnoser]
public class RouteCompositionBenchmarks
{
    private static readonly MessageListRequest FullQuery = new()
    {
        Limit = "50",
        Order = ListOrder.Descending,
        Cursor = "cur_0123456789abcdef",
    };

    /// <summary>The constant no-parameter route.</summary>
    [Benchmark]
    public string ConstantRoute() => OpenCodeRoutes.Health.Get;

    /// <summary>The two-parameter path route: segment concatenation plus value escaping.</summary>
    [Benchmark]
    public string PathRoute() =>
        OpenCodeRoutes.Sessions.GetMessage("ses_bench0000000000000000001", "msg_bench0000000000000000001");

    /// <summary>The query-bearing list route: builder, escaping, and final concatenation.</summary>
    [Benchmark]
    public string QueryRoute() =>
        OpenCodeRoutes.Sessions.ListMessages("ses_bench0000000000000000001", FullQuery);
}
