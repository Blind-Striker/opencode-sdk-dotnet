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
}
