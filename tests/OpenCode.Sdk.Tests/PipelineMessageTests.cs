using System.Net;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class PipelineMessageTests
{
    [Test]
    public async Task Dispose_Should_Release_The_Request_And_The_Response()
    {
        using var requestContent = new DisposalTrackingContent("{}");
        using var responseContent = new DisposalTrackingContent("{}");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://localhost:4096/api/session"))
        {
            Content = requestContent,
        };
        using var response = new DisposalTrackingResponse(HttpStatusCode.OK, responseContent);
        var message = new PipelineMessage
        {
            Request = request,
            Response = response,
        };

        message.Dispose();

        await Assert.That(requestContent.IsDisposed).IsTrue();
        await Assert.That(response.IsDisposed).IsTrue();
        await Assert.That(responseContent.IsDisposed).IsTrue();
    }

    [Test]
    public async Task Dispose_Should_Tolerate_A_Message_Without_A_Response()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://localhost:4096/api/health"));
        var message = new PipelineMessage { Request = request };

        message.Dispose();

        await Assert.That(message.Response).IsNull();
    }
}
