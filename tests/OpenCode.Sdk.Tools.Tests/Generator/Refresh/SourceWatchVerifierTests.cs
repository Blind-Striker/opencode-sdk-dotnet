using OpenCode.Sdk.Tools.Generator.Refresh;
using OpenCode.Sdk.Tools.Generator.Refresh.Models;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Refresh;

public sealed class SourceWatchVerifierTests
{
    private static readonly WatchedSource Pinned =
        RefreshScenarioData.Watched("packages/core/src/pty/ticket.ts", "const DEFAULT_TTL = Duration.seconds(60)\n", "seconds(60)");

    [Test]
    public async Task Compare_Should_Report_Nothing_When_The_Pin_Reproduces()
    {
        var problems = SourceWatchVerifier.Compare([Pinned], [Observed(Pinned.Sha256, anchorMatched: true)]);

        await Assert.That(problems).IsEmpty();
    }

    [Test]
    public async Task Compare_Should_Report_A_Changed_Blob()
    {
        var problems = SourceWatchVerifier.Compare([Pinned], [Observed(new string('a', 64), anchorMatched: true)]);

        await Assert.That(problems.Single()).Contains("changed: pinned");
        await Assert.That(problems.Single()).Contains(Pinned.Behavior);
    }

    [Test]
    public async Task Compare_Should_Report_A_Lost_Anchor()
    {
        var problems = SourceWatchVerifier.Compare([Pinned], [Observed(Pinned.Sha256, anchorMatched: false)]);

        await Assert.That(problems.Single()).Contains("no longer carries its anchor (contains 'seconds(60)')");
    }

    [Test]
    public async Task Compare_Should_Report_A_Source_The_Observation_Skipped()
    {
        var problems = SourceWatchVerifier.Compare([Pinned], []);

        await Assert.That(problems.Single()).Contains("was not observed");
    }

    private static ReceiptWatchedSource Observed(string sha256, bool anchorMatched) =>
        new()
        {
            Path = Pinned.Path,
            Sha256 = sha256,
            AnchorMatched = anchorMatched,
        };
}
