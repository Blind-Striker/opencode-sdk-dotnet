# 📑 Pagination

Two operations in the API return more than fits in one response, and both use the same opaque
cursor envelope: **listing a session's messages** and **listing sessions**. Everything else returns
its whole answer at once.

- [🧾 The cursor envelope](#-the-cursor-envelope)
- [🎛️ Shaping the request](#️-shaping-the-request)
- [🔁 Paging by hand](#-paging-by-hand)
- [♾️ Or let the SDK page for you](#️-or-let-the-sdk-page-for-you)
- [⚖️ Which one to use](#️-which-one-to-use)

## 🧾 The cursor envelope

| Response | Items | Cursor |
|---|---|---|
| `MessageListResponse` | `Messages` (`IReadOnlyList<ISessionMessageInfo>`) | `Cursor` |
| `SessionListResponse` | `Sessions` (`IReadOnlyList<SessionInfo>`) | `Cursor` |

`ListCursor` has two members, both nullable strings:

```text
Next      the cursor for the following page, or null when there is no following page
Previous  the cursor for the preceding page, available on explicit page calls
```

**A missing `Next` is the one and only end signal.** Do not infer the end from an empty page — an
empty page that still carries a `Next` means "keep going". And do not read a cursor: it is opaque,
never to be decoded, compared, incremented, or deduplicated. Hand it back exactly as you got it.

Both responses are ordinary [response envelopes](errors-and-responses.md#-the-response-spine), so a
page carries `Status`, `IsError`, `Error`, and `RawBody` and accepts per-call `NoThrow`:

```csharp
var page = await sessions.ListSessionsAsync(
    new SessionListRequest { Limit = "25" },
    OpenCodeRequestOptions.NoThrow);

Console.WriteLine(page.IsError
    ? $"HTTP {page.Status}: {page.Error?.Tag ?? "<untyped>"}"
    : $"{page.Sessions.Count} sessions, next cursor {page.Cursor.Next ?? "<none>"}");
```

## 🎛️ Shaping the request

Three channels do the paging, and both request types carry all three:

| Member | Type | Meaning |
|---|---|---|
| `Limit` | `string?` | Page size. A string, because that is how the API declares the query parameter — pass `"50"`, not `50`. |
| `Order` | `ListOrder?` | `Ascending` or `Descending`. **First page only.** |
| `Cursor` | `string?` | The opaque continuation from the previous page's `Cursor.Next`. |

`MessageListRequest` inherits them from the shared abstract `ListRequest`.
`SessionListRequest` declares the same three itself, next to its own filters — `Search`,
`Project`, `Workspace`, `Directory`, `Subpath`, and `ParentId`.

> **🧭 `Order` belongs to the first request.** The order is fixed when the sequence starts; a
> continuation carries `Limit` and `Cursor` only. Keeping `Order` on later pages is not something
> the SDK will correct for you — the snippets below drop it deliberately.

## 🔁 Paging by hand

Ask for a page, use it, and stop when `Next` is gone:

```csharp
var session = client.Sessions.GetSessionClient(sessionId);
var request = new MessageListRequest { Limit = "50", Order = ListOrder.Ascending };

while (true)
{
    var page = await session.ListMessagesAsync(request);

    foreach (var message in page.Messages)
    {
        Console.WriteLine($"{message.GetType().Name} ({message.Type})");
    }

    if (page.Cursor.Next is not { } next)
    {
        break;
    }

    request = new MessageListRequest { Limit = request.Limit, Cursor = next };
}
```

Sessions page identically — the only differences are the request type and the item collection:

```csharp
var request = new SessionListRequest { Limit = "25", Order = ListOrder.Descending };

while (true)
{
    var page = await client.Sessions.ListSessionsAsync(request);

    foreach (var session in page.Sessions)
    {
        Console.WriteLine($"{session.Id}  {session.Title}");
    }

    if (page.Cursor.Next is not { } next)
    {
        break;
    }

    request = new SessionListRequest { Limit = request.Limit, Cursor = next };
}
```

Manual paging is what you want when you need the page metadata: the status of each request,
`NoThrow` handling per page, or `Cursor.Previous` to walk backwards.

## ♾️ Or let the SDK page for you

`SessionClient.EnumerateMessagesAsync` is the automatic companion to `ListMessagesAsync`. It yields
the **items**, lazily, following `Next` for you:

```csharp
var stream = session.EnumerateMessagesAsync(new MessageListRequest
{
    Limit = "50",
    Order = ListOrder.Ascending,
});

await foreach (var message in stream.WithCancellation(cancellationToken))
{
    Console.WriteLine($"{message.GetType().Name} ({message.Type})");
}
```

What it does with your request is exactly the manual loop above: the first request goes out
unchanged, every continuation keeps your `Limit`, drops `Order`, and sends the returned cursor
verbatim. It stops when a page comes back without a `Next`.

Three things to know:

- **It is a pull sequence over ordinary HTTP calls**, not a stream. Nothing is held open between
  pages, and stopping early (a `break`, a `return`, a cancelled token) simply means the next page is
  never requested.
- **It always throws.** There is no envelope to hand you, so an API error on page seven throws
  `OpenCodeApiException` out of the `await foreach` — `NoThrow` is not available here.
- **Cancellation reaches every request** and is also checked between buffered items, so a token you
  cancel mid-page takes effect immediately rather than after the current page drains.

`EnumerateMessagesAsync` is the **only** automatic companion in the SDK today. Session listing has
no `EnumerateSessionsAsync`; page it with the loop above.

## ⚖️ Which one to use

| | `List*Async` | `EnumerateMessagesAsync` |
|---|---|---|
| You get | one page envelope | the items, across all pages |
| Page metadata (`Status`, `Cursor`) | ✅ yes | ❌ no |
| Per-call `NoThrow` | ✅ yes | ❌ no — always throws |
| Backwards paging via `Previous` | ✅ yes | ❌ no |
| Cursor bookkeeping | yours | handled |

Reach for `EnumerateMessagesAsync` when you just want the messages. Reach for `ListMessagesAsync`
when the page itself is part of what you are doing — rendering a paged UI, checkpointing a cursor,
or treating a failed page as data.
