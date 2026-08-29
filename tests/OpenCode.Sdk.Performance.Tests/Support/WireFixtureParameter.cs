using BenchmarkDotNet.Running;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// Resolves the <see cref="WireFixture"/> a benchmark case consumes from its parameters. This is
/// the single lookup the wire columns and <see cref="WireFixtureDiagnoser"/> share, so a rendered
/// column and an exported metric can never disagree about which fixture a case ran against.
/// </summary>
internal static class WireFixtureParameter
{
    public static WireFixture? Find(BenchmarkCase benchmarkCase)
    {
        ArgumentNullException.ThrowIfNull(benchmarkCase);
        return benchmarkCase.Parameters.Items.Select(static parameter => parameter.Value).OfType<WireFixture>().FirstOrDefault();
    }
}
