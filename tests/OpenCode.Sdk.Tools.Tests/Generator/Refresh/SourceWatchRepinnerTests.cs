using OpenCode.Sdk.Tools.Generator.Refresh;
using OpenCode.Sdk.Tools.Generator.Refresh.Models;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Refresh;

public sealed class SourceWatchRepinnerTests
{
    private static readonly WatchedSource Pinned =
        RefreshScenarioData.Watched("packages/core/src/pty/ticket.ts", "const DEFAULT_TTL = Duration.seconds(60)\n", "seconds(60)");

    [Test]
    public async Task Repin_Should_Carry_The_Observed_Hashes_Onto_The_Watch()
    {
        var moved = new string('c', 64);

        var repinned = SourceWatchRepinner.Repin(
            RefreshScenarioData.Watch(1, Pinned), [Observed(moved, anchorMatched: true)]);

        await Assert.That(repinned.Sources.Single().Sha256).IsEqualTo(moved);
        await Assert.That(repinned.Sources.Single().Anchor.Text).IsEqualTo(Pinned.Anchor.Text);
        await Assert.That(repinned.SchemaVersion).IsEqualTo(1);
    }

    [Test]
    public async Task Repin_Should_Refuse_A_Receipt_Observing_Other_Files()
    {
        var exception = await Assert
            .That(() => SourceWatchRepinner.Repin(RefreshScenarioData.Watch(1, Pinned), []))
            .Throws<SnapshotRefreshException>();

        await Assert.That(exception!.Message).Contains("re-run prepare");
    }

    [Test]
    public async Task Repin_Should_Refuse_A_Lost_Anchor()
    {
        var exception = await Assert
            .That(() => SourceWatchRepinner.Repin(
                RefreshScenarioData.Watch(1, Pinned), [Observed(Pinned.Sha256, anchorMatched: false)]))
            .Throws<SnapshotRefreshException>();

        await Assert.That(exception!.Message).Contains("lost anchors in packages/core/src/pty/ticket.ts");
    }

    private static ReceiptWatchedSource Observed(string sha256, bool anchorMatched) =>
        new()
        {
            Path = Pinned.Path,
            Sha256 = sha256,
            AnchorMatched = anchorMatched,
        };
}
