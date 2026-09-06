using System.Globalization;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

[ClassDataSource<SimulatedDriveServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class SkillsClientLiveTests(SimulatedDriveServerFixture server)
{
    private const string SkillDescription = "Exercises local skill discovery for SDK live verification.";
    private const string SkillId = "sdk-live-skill";
    private const string SkillName = "SDK live skill";
    private const string SkillContent = "Use this skill to verify local SDK skill discovery.";

    [Test]
    [Timeout(60_000)]
    public async Task ListSkillsAsync_Should_Report_The_Workspace_Skill(CancellationToken cancellationToken)
    {
        using var workspace = server.CreateWorkspace();
        var fixture = new FixtureLoader().LoadText("Skills.sdk-live-skill.md");
        var skillPath = workspace.WriteTextFile(".opencode/skills/sdk-live-skill/SKILL.md", fixture);
        using var client = server.CreateClient(new LocationSelector { Directory = workspace.Path });
        var settled = await client.Plugins.AwaitPluginActivationAsync(cancellationToken: cancellationToken);
        await Assert.That(settled.Status).IsEqualTo(204);

        var response = await client.Skills.ListSkillsAsync(cancellationToken: cancellationToken);

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Location.Directory).IsEqualTo(workspace.Path);
        var skill = response.Skills.Single(item => item.Id == SkillId);
        await Assert.That(skill.Name).IsEqualTo(SkillName);
        await Assert.That(skill.Description).IsEqualTo(SkillDescription);
        await Assert.That(skill.Location).IsEqualTo(skillPath);
        await Assert.That(skill.Content).IsEqualTo(SkillContent);

        Console.WriteLine(
            "skills-live: status=" + Number(response.Status) + " id=" + skill.Id + " location=" + skill.Location);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
