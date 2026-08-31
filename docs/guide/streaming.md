# 📡 Streaming

Two server-sent event streams, both surfaced as `IAsyncEnumerable<T>` and both riding the same
transport as ordinary calls: the **global event bus** for everything happening in the server
process, and the **per-session log** for one conversation.

- [🔊 The global event bus](#-the-global-event-bus)
- [📜 A single session's log](#-a-single-sessions-log)
- [🧩 Unknown events do not break your consumer](#-unknown-events-do-not-break-your-consumer)
- [⏹️ Cancellation and lifetime](#️-cancellation-and-lifetime)
- [💥 Streams always throw](#-streams-always-throw)

## 🔊 The global event bus

`EventsClient.SubscribeAsync(CancellationToken)` yields `IEvent` — the union of every event the
pinned snapshot declares. Pattern-match the ones you care about and let the rest fall through:

```csharp
using var window = new CancellationTokenSource(TimeSpan.FromMinutes(5));

await foreach (var @event in client.Events.SubscribeAsync(window.Token))
{
    switch (@event)
    {
        case SessionIdle idle:
            Console.WriteLine($"session {idle.Data.SessionId} went idle");
            break;
        case UnknownEvent unknown:
            Console.WriteLine($"unknown event {unknown.Type}: {unknown.Payload.GetRawText()}");
            break;
        default:
            Console.WriteLine($"{@event.GetType().Name} ({@event.Type})");
            break;
    }
}
```

`IEvent` carries exactly one common member, `Type` — the wire discriminator. Every concrete event
type adds its own payload, so a `switch` on the type is the way in; `@event.Type` is there for
logging and for the frames you have not written a case for.

**This bus is live and volatile, by the server's design, not the SDK's.** It has no filter, no
cursor, no replay, and no resume channel:

- Events published while you were disconnected are **gone**. There is no backfill to ask for.
- A consumer slower than the producer can overflow and fail the stream.
- The SDK **never auto-reconnects**. That is deliberate — a silent reconnect would hide exactly the
  gap you need to know about. After a failure, refresh whatever state you care about with ordinary
  calls, then subscribe again.

If you need durable history for one conversation, the per-session log is the stream that has it.

## 📜 A single session's log

`SessionClient.GetLogAsync(SessionLogRequest?, CancellationToken)` yields `ISessionLogItem` for one
session:

```csharp
var session = client.Sessions.GetSessionClient(sessionId);

await foreach (var item in session.GetLogAsync(new SessionLogRequest { Follow = QueryBoolean.True }, cancellationToken))
{
    Console.WriteLine($"{item.GetType().Name} ({item.Type})");
}
```

`SessionLogRequest` has two members and both matter:

| Member | Type | Meaning |
|---|---|---|
| `Follow` | `QueryBoolean?` | `True` keeps the stream open and keeps delivering; otherwise the stream ends when the existing log has been sent. |
| `After` | `string?` | Continue after an item you have already seen — the explicit continuation channel the global bus does not have. |

So a consumer that survives a restart records the last item it processed and asks for what came
after it:

```csharp
var request = new SessionLogRequest
{
    After = lastSeenId,
    Follow = QueryBoolean.True,
};
```

> **📎 A note on guarantees**: `after` is a *request*, not a durability promise. How much history
> the server retains, and for how long, is upstream's business and is not something this SDK can
> state on its behalf. Treat a returned gap as possible and reconcile with ordinary reads.

## 🧩 Unknown events do not break your consumer

The SDK is generated from a **pinned** snapshot of the opencode API, while the server you are
talking to may be newer. When a frame arrives with a `type` this build has never heard of, the
stream does not fail and does not skip it — it hands you `UnknownEvent`, carrying the raw `Type`
string and the untouched JSON body as a `JsonElement`:

```csharp
case UnknownEvent unknown:
    Console.WriteLine($"unknown event {unknown.Type}: {unknown.Payload.GetRawText()}");
    break;
```

The per-session log has the same escape hatch, `UnknownSessionLogItem`, with the same two members.
Practically: a server upgrade cannot break your event loop, you can log or even handle a new event
type before the SDK is regenerated, and when it is regenerated the frame simply arrives as its
typed self instead.

What this is *not* is a bucket for malformed data. A frame whose `type` is known but whose body
cannot be read is a protocol failure and throws — the carrier is for *unknown*, not for *broken*.

## ⏹️ Cancellation and lifetime

- **Streams open lazily.** Calling `SubscribeAsync` or `GetLogAsync` does not touch the network;
  the request goes out on the first `MoveNextAsync`, which is to say on the first `await foreach`.
- **The enumeration token is the off switch.** Cancelling it closes the stream and surfaces as
  `OperationCanceledException` in the usual place. A `CancellationTokenSource` with a timeout, as
  in the first snippet, is a perfectly good session budget.
- **`WithCancellation` works** where you did not pass a token at the call — `await foreach (var x
  in stream.WithCancellation(token))`.
- **Disposing the enumerator ends the connection**, which `await foreach` does for you on `break`,
  on `return`, and on an exception.
- A stream that is slow but still flowing stays alive; one that stalls completely is failed by an
  internal progress window rather than hanging forever.

## 💥 Streams always throw

Streaming operations return a stream, not a response envelope — so they have **no `requestOptions`
parameter**, and [`NoThrow`](errors-and-responses.md#-ask-for-the-failure-as-data-instead) is not
available on them. There is no envelope to put an error on, so every failure is an exception:

```csharp
try
{
    await foreach (var @event in client.Events.SubscribeAsync(cancellationToken))
    {
        Console.WriteLine(@event.Type);
    }
}
catch (OpenCodeStreamFailureException failure)
{
    Console.WriteLine($"the server ended the stream: {failure.Cause.Count} declared cause(s)");
}
catch (OpenCodeTransportException transport)
{
    Console.WriteLine($"the stream broke: {transport.Message}");
}
```

- `OpenCodeStreamFailureException` means the **server** ended the stream with a declared failure
  frame; its `Cause` collection carries the typed causes and is never null.
- `OpenCodeTransportException` — its base class — covers everything else: a broken connection, a
  body cut mid-event, an undecodable frame, a stalled read. Catching the base handles both.
- `OperationCanceledException` stays itself. Your cancellation is never repackaged as a failure.

Ordering matters in that `catch` chain: `OpenCodeStreamFailureException` derives from
`OpenCodeTransportException`, so the specific one goes first.
