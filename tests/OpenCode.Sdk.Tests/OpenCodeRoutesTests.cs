using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests;

public sealed class OpenCodeRoutesTests
{
    [Test]
    [Arguments(".")]
    [Arguments("..")]
    [Arguments(" ")]
    public async Task GetMessage_Should_Refuse_An_Unsafe_Session_Segment(string sessionId)
    {
        var exception = Assert.Throws<ArgumentException>(() => _ = OpenCodeRoutes.Sessions.GetMessage(sessionId, "m"));

        await Assert.That(exception.ParamName).IsEqualTo("sessionId");
    }

    [Test]
    public async Task GetMessage_Should_Refuse_Lone_Surrogates()
    {
        string[] sessionIds = ["\ud800", "\udc00"];
        foreach (var sessionId in sessionIds)
        {
            var exception = Assert.Throws<ArgumentException>(() => _ = OpenCodeRoutes.Sessions.GetMessage(sessionId, "m"));

            await Assert.That(exception.ParamName).IsEqualTo("sessionId");
        }
    }

    [Test]
    [Arguments(".")]
    [Arguments("..")]
    [Arguments(" ")]
    public async Task GetMessage_Should_Refuse_An_Unsafe_Message_Segment(string messageId)
    {
        var exception = Assert.Throws<ArgumentException>(() => _ = OpenCodeRoutes.Sessions.GetMessage("s", messageId));

        await Assert.That(exception.ParamName).IsEqualTo("messageId");
    }

    [Test]
    public async Task GetMessage_Should_Escape_Reserved_Characters()
    {
        var route = OpenCodeRoutes.Sessions.GetMessage("a b", "c/d");

        await Assert.That(route).IsEqualTo("/api/session/a%20b/message/c%2Fd");
    }

    [Test]
    public async Task ListSessions_Should_Refuse_An_Oversized_Query_Value()
    {
        var request = new SessionListRequest { Search = new string('a', 32_767) };

        var exception = Assert.Throws<ArgumentException>(() => _ = OpenCodeRoutes.Sessions.ListSessions(request));

        await Assert.That(exception.ParamName).IsEqualTo("search");
    }

    [Test]
    public async Task GetMessage_Should_Accept_A_Valid_Surrogate_Pair()
    {
        var route = OpenCodeRoutes.Sessions.GetMessage("emoji-\ud83d\ude00", "m");

        await Assert.That(route).IsEqualTo("/api/session/emoji-%F0%9F%98%80/message/m");
    }

    [Test]
    public async Task FindEntries_Should_Carry_The_Required_Query_Alone_When_Nothing_Else_Is_Set()
    {
        var route = OpenCodeRoutes.FileSystem.FindEntries(new FsFindRequest { Query = "todo" });

        await Assert.That(route).IsEqualTo("/api/fs/find?query=todo");
    }

    [Test]
    public async Task FindEntries_Should_Carry_Every_Member_In_Wire_Order()
    {
        var route = OpenCodeRoutes.FileSystem.FindEntries(new FsFindRequest
        {
            Location = new LocationSelector { Workspace = "wrk_1" },
            Query = "todo",
            Type = FsFindRequestType.Directory,
            Limit = "25",
        });

        await Assert.That(route).IsEqualTo("/api/fs/find?location[workspace]=wrk_1&query=todo&type=directory&limit=25");
    }

    [Test]
    public async Task FindEntries_Should_Refuse_A_Null_Request()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => _ = OpenCodeRoutes.FileSystem.FindEntries(null!));

        await Assert.That(exception.ParamName).IsEqualTo("request");
    }

    [Test]
    public async Task GetDiff_Should_Spell_The_Committed_Mode_And_Its_Base()
    {
        var route = OpenCodeRoutes.Vcs.GetDiff(new VcsDiffRequest { Mode = VcsMode.Committed, Base = "main" });

        await Assert.That(route).IsEqualTo("/api/vcs/diff?mode=committed&base=main");
    }

    [Test]
    [Arguments(VcsMode.Working, "working")]
    [Arguments(VcsMode.Branch, "branch")]
    [Arguments(VcsMode.Committed, "committed")]
    public async Task GetDiff_Should_Spell_Every_Mode_The_Way_The_Wire_Declares_It(VcsMode mode, string wireValue)
    {
        var route = OpenCodeRoutes.Vcs.GetDiff(new VcsDiffRequest { Mode = mode });

        await Assert.That(route).IsEqualTo($"/api/vcs/diff?mode={wireValue}");
    }

    [Test]
    public async Task GetStats_Should_Spell_The_Tools_Detail_Value()
    {
        var route = OpenCodeRoutes.Sessions.GetStats(new SessionStatsRequest { Tools = SessionStatsRequestTools.Detail });

        await Assert.That(route).IsEqualTo("/api/session/stats?tools=detail");
    }

    [Test]
    public async Task GetStats_Should_Return_The_Bare_Path_When_Nothing_Is_Set()
    {
        await Assert.That(OpenCodeRoutes.Sessions.GetStats(new SessionStatsRequest())).IsEqualTo("/api/session/stats");
        await Assert.That(OpenCodeRoutes.Sessions.GetStats()).IsEqualTo("/api/session/stats");
    }

    /// <summary>
    /// The wire declares 'cursor' and 'limit' as strings carrying a numeric pattern; patterns are
    /// never validated client-side (ADR-0014), so the builder writes what the caller handed it.
    /// </summary>
    [Test]
    public async Task GetOutput_Should_Carry_The_Cursor_And_Limit_As_The_Strings_They_Are()
    {
        var route = OpenCodeRoutes.Shells.GetOutput("sh_100", new ShellOutputRequest
        {
            Location = new LocationSelector { Workspace = "wrk_1" },
            Cursor = "1.5e3",
            Limit = "+0",
        });

        await Assert.That(route).IsEqualTo("/api/shell/sh_100/output?location[workspace]=wrk_1&cursor=1.5e3&limit=%2B0");
    }

    [Test]
    public async Task GetOutput_Should_Return_The_Bare_Path_When_Nothing_Is_Set()
    {
        await Assert.That(OpenCodeRoutes.Shells.GetOutput("sh_100", new ShellOutputRequest()))
            .IsEqualTo("/api/shell/sh_100/output");
        await Assert.That(OpenCodeRoutes.Shells.GetOutput("sh_100")).IsEqualTo("/api/shell/sh_100/output");
    }

    [Test]
    public async Task Worktree_Routes_Should_Escape_The_Project_Segment()
    {
        await Assert.That(OpenCodeRoutes.Worktrees.CreateWorktree("a b")).IsEqualTo("/api/worktree/a%20b");
        await Assert.That(OpenCodeRoutes.Worktrees.ListWorktrees("a b")).IsEqualTo("/api/worktree/a%20b");
        await Assert.That(OpenCodeRoutes.Worktrees.RefreshWorktrees("a b")).IsEqualTo("/api/worktree/a%20b/refresh");
        await Assert.That(OpenCodeRoutes.Worktrees.RemoveWorktree("a b")).IsEqualTo("/api/worktree/a%20b");
    }

    [Test]
    [Arguments(".")]
    [Arguments("..")]
    [Arguments(" ")]
    public async Task RefreshWorktrees_Should_Refuse_An_Unsafe_Project_Segment(string projectId)
    {
        var exception = Assert.Throws<ArgumentException>(() => _ = OpenCodeRoutes.Worktrees.RefreshWorktrees(projectId));

        await Assert.That(exception.ParamName).IsEqualTo("projectId");
    }
}
