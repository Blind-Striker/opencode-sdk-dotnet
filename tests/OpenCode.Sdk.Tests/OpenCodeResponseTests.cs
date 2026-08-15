using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class OpenCodeResponseTests
{
    [Test]
    public async Task Constructor_Should_Default_To_The_Success_Path()
    {
        var response = new EmptyResponse
        {
            Status = 200,
        };

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Error).IsNull();
        await Assert.That(response.RawBody).IsNull();
    }

    [Test]
    public async Task SessionListResponse_Should_Refuse_A_Null_Page_Element()
    {
        var session = new GeneratedJsonSerializer()
            .Deserialize<SessionInfo>(new FixtureLoader().LoadJson("Serialization.known-session.json"));

        var exception = Assert.Throws<ArgumentException>(() => _ = new SessionListResponse
        {
            Status = 200,
            Sessions = [session, null!],
            Cursor = new ListCursor(),
        });

        await Assert.That(exception.Message).Contains("null element");
    }

    private sealed record EmptyResponse : OpenCodeResponse;
}
