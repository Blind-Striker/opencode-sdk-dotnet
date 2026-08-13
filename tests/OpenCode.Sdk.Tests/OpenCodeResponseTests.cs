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

    private sealed record EmptyResponse : OpenCodeResponse;
}
