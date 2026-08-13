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
}
