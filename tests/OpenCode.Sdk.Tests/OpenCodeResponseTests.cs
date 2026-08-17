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
    public async Task SessionListResponse_Should_Retain_The_Caller_Owned_Page_Reference()
    {
        var session = new GeneratedJsonSerializer()
            .Deserialize<SessionInfo>(new FixtureLoader().LoadJson("Serialization.known-session.json"));
        var sessions = new List<SessionInfo> { session, };
        var response = new SessionListResponse
        {
            Status = 200,
            Sessions = sessions,
            Cursor = new ListCursor(),
        };

        sessions.Add(null!);

        await Assert.That(response.Sessions).Count().IsEqualTo(2);
        await Assert.That(response.Sessions[1]).IsNull();
    }

    private sealed record EmptyResponse : OpenCodeResponse;
}
