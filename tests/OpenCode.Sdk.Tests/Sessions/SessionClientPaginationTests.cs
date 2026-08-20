namespace OpenCode.Sdk.Tests;

public sealed class SessionClientPaginationTests
{
    [Test]
    public async Task EnumerateMessagesAsync_Should_Use_The_Virtual_Page_Method_And_Preserve_An_Empty_Cursor()
    {
        var client = new StubSessionClient(Page(next: string.Empty), Page(next: null));
        var initialRequest = new MessageListRequest
        {
            Limit = "2",
            Order = ListOrder.Descending,
            Cursor = "cur_start",
        };
        var itemCount = 0;

        await foreach (var _ in client
                           .EnumerateMessagesAsync(initialRequest, CancellationToken.None)
                           .WithCancellation(CancellationToken.None))
        {
            itemCount++;
        }

        await Assert.That(itemCount).IsEqualTo(0);
        await Assert.That(client.Requests.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(client.Requests[0], initialRequest)).IsTrue();
        await Assert.That(client.Requests[1]?.Limit).IsEqualTo("2");
        await Assert.That(client.Requests[1]?.Order).IsNull();
        await Assert.That(client.Requests[1]?.Cursor).IsEqualTo(string.Empty);
    }

    private static MessageListResponse Page(string? next) => new()
    {
        Status = 200,
        Messages = [],
        Cursor = new ListCursor { Next = next, },
    };

    private sealed class StubSessionClient(params MessageListResponse[] pages) : SessionClient
    {
        private readonly Queue<MessageListResponse> _pages = new(pages);

        public List<MessageListRequest?> Requests { get; } = [];

        public override Task<MessageListResponse> ListMessagesAsync(MessageListRequest? request = null,
            OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_pages.Dequeue());
        }
    }
}
