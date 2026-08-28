using System.Globalization;
using OpenCode.Sdk.Tools.Output;

namespace OpenCode.Sdk.Tools.Tests.Output;

/// <summary>
/// <see cref="CliWrapProjectFormatter.Batch"/> is the pure splitting logic behind the
/// formatter's per-invocation command line; the real process launch is exercised by running
/// <c>generate</c> itself, never faked here.
/// </summary>
public sealed class CliWrapProjectFormatterTests
{
    [Test]
    public async Task Batch_Should_Return_Nothing_For_An_Empty_Input()
    {
        var batches = CliWrapProjectFormatter.Batch([], 100).ToArray();

        await Assert.That(batches.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Batch_Should_Keep_Paths_Together_When_They_Fit_One_Budget()
    {
        string[] paths = ["Models/A.cs", "Models/B.cs", "Models/C.cs"];

        var batches = CliWrapProjectFormatter.Batch(paths, 100).ToArray();

        await Assert.That(batches.Length).IsEqualTo(1);
        await Assert.That(batches[0]).IsEquivalentTo(paths);
    }

    [Test]
    public async Task Batch_Should_Split_Once_The_Running_Length_Would_Exceed_The_Budget()
    {
        // Each path is 10 characters ("0123456789" + one joining space = 11); a budget of 25
        // fits two paths (22) but refuses a third (33), so the third opens a new batch.
        string[] paths = ["0123456789", "abcdefghij", "ABCDEFGHIJ", "klmnopqrst"];

        var batches = CliWrapProjectFormatter.Batch(paths, 25).ToArray();

        await Assert.That(batches.Length).IsEqualTo(2);
        await Assert.That(batches[0]).IsEquivalentTo(["0123456789", "abcdefghij"]);
        await Assert.That(batches[1]).IsEquivalentTo(["ABCDEFGHIJ", "klmnopqrst"]);
    }

    [Test]
    public async Task Batch_Should_Ship_One_Oversized_Path_Alone_Rather_Than_Drop_It()
    {
        var oversized = new string('x', 500);
        string[] paths = ["Models/A.cs", oversized, "Models/B.cs"];

        var batches = CliWrapProjectFormatter.Batch(paths, 25).ToArray();

        await Assert.That(batches.Length).IsEqualTo(3);
        await Assert.That(batches[0]).IsEquivalentTo(["Models/A.cs"]);
        await Assert.That(batches[1]).IsEquivalentTo([oversized]);
        await Assert.That(batches[2]).IsEquivalentTo(["Models/B.cs"]);
    }

    [Test]
    public async Task Batch_Should_Preserve_Input_Order_Across_Every_Batch()
    {
        var paths = Enumerable.Range(0, 50)
            .Select(static index => $"Models/Type{index.ToString(CultureInfo.InvariantCulture)}.cs")
            .ToArray();

        var batches = CliWrapProjectFormatter.Batch(paths, 60).ToArray();
        var flattened = batches.SelectMany(static batch => batch).ToArray();

        await Assert.That(flattened).IsEquivalentTo(paths);
    }
}
