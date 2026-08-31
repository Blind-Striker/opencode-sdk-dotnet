# 🧱 Errors and responses

Every one-shot call answers with a **response envelope** that carries the same four members no
matter which operation produced it, and every failure is either a typed API error you can branch on
or a transport failure you cannot. Nothing is stringly typed, and nothing is swallowed.

- [🧩 The response spine](#-the-response-spine)
- [💥 Calls throw by default](#-calls-throw-by-default)
- [🤝 Ask for the failure as data instead](#-ask-for-the-failure-as-data-instead)
- [🏷️ The typed error family](#️-the-typed-error-family)
- [🔍 Guarded payload accessors](#-guarded-payload-accessors)
- [🔌 Transport failures are a different plane](#-transport-failures-are-a-different-plane)
- [🚀 When the launcher fails](#-when-the-launcher-fails)

## 🧩 The response spine

Every response type derives from `OpenCodeResponse`:

| Member | Type | Meaning |
|---|---|---|
| `Status` | `int` | The HTTP status the server answered with. |
| `IsError` | `bool` | Whether this response is a failure. The guard for everything below. |
| `Error` | `IOpenCodeError?` | The typed error payload, when the server sent one the SDK could type. |
| `RawBody` | `string?` | The exact response body, retained on failures — including when typed parsing did not succeed. |

On top of the spine each response adds its own payload members: `SessionResponse.Session`,
`SessionListResponse.Sessions` and `.Cursor`, `HealthResponse.Health`, and so on.

## 💥 Calls throw by default

Nothing is silent. A declared API failure throws `OpenCodeApiException`, which carries the same
three facts as the spine:

```csharp
try
{
    var session = await client.Sessions.GetSessionClient("ses_missing").GetSessionAsync();

    Console.WriteLine(session.Session.Title);
}
catch (OpenCodeApiException failure)
{
    Console.WriteLine($"HTTP {failure.Status}: {failure.Error?.Tag ?? "<untyped>"}");
    Console.WriteLine(failure.RawBody);
}
```

`OpenCodeApiException.Status`, `.Error`, and `.RawBody` mean exactly what their spine counterparts
mean — including `RawBody`, so an error you could not type is still fully inspectable. The whole
family descends from `OpenCodeException`, so one `catch` covers everything the SDK throws on
purpose:

```text
OpenCodeException
├── OpenCodeApiException          declared API failure (has Status / Error / RawBody)
├── OpenCodeTransportException    the call never produced a usable response
│   └── OpenCodeStreamFailureException   a stream ended with a declared failure frame
└── OpenCodeServerException       the standalone launcher could not start or keep a server
```

## 🤝 Ask for the failure as data instead

When a 404 is a normal answer rather than an accident, pass `OpenCodeRequestOptions.NoThrow` and
branch on the envelope:

```csharp
var response = await client.Sessions
    .GetSessionClient("ses_missing")
    .GetSessionAsync(OpenCodeRequestOptions.NoThrow);

if (response.IsError)
{
    Console.WriteLine($"HTTP {response.Status}: {response.Error?.Tag ?? "<untyped>"}");
    return;
}

Console.WriteLine(response.Session.Title);
```

`NoThrow` is a **per-call** decision — the static `OpenCodeRequestOptions.NoThrow` is a ready-made
instance, and `new OpenCodeRequestOptions { ErrorBehavior = ErrorBehavior.NoThrow }` is the same
thing spelled out. There is deliberately no client-level switch: whether a failure is exceptional
depends on the call, not on the client.

Two limits worth internalising:

- **`NoThrow` covers declared API errors only.** It never suppresses a transport failure.
- **Streaming operations have no `requestOptions` parameter at all** — a stream has no envelope to
  put an error on, so it always throws. See [streaming](streaming.md#-streams-always-throw).

`OpenCodeRequestOptions` also carries `Location`, the per-call project override, which merges over
the client's ambient location member by member — a set member wins, an unset one inherits.

## 🏷️ The typed error family

`Error` is `IOpenCodeError`, whose only common member is `Tag` — the wire discriminator. Concrete
error types add their own data, so `switch` on the type:

```csharp
switch (response.Error)
{
    case SessionNotFoundError notFound:
        Console.WriteLine($"no session {notFound.SessionId}");
        break;
    case UnauthorizedError unauthorized:
        Console.WriteLine($"credential rejected: {unauthorized.Message}");
        break;
    case WorktreeError worktree:
        Console.WriteLine($"worktree refused: {worktree.Data.Message} (force required: {worktree.Data.ForceRequired == true})");
        break;
    case UnknownOpenCodeError unknown:
        Console.WriteLine($"unknown error {unknown.Tag}: {unknown.Payload.GetRawText()}");
        break;
    case null:
        Console.WriteLine("the failure carried no typed error");
        break;
    default:
        Console.WriteLine(response.Error.Tag);
        break;
}
```

There are roughly two dozen of them — `SessionNotFoundError`, `MessageNotFoundError`,
`PtyNotFoundError`, `AgentNotFoundError`, `ProviderNotFoundError`, `InvalidRequestError`,
`InvalidCursorError`, `ConflictError`, `ForbiddenError`, `UnauthorizedError`, `SessionBusyError`,
`ServiceUnavailableError`, and friends — each with the members its own failure actually carries
(`SessionNotFoundError.SessionId`, `InvalidRequestError.Field` and `.Kind`,
`ServiceUnavailableError.Service`).

### Two wire dialects, one interface

Upstream spells its errors two different ways, and the SDK represents both faithfully rather than
flattening them:

- **The `_tag` dialect** — the common one. The discriminator rides a `_tag` property and the error's
  data sits alongside it: `SessionNotFoundError` has `Message` and `SessionId` directly on the type.
- **The `{name, data}` dialect** — used by the worktree family. The discriminator rides `name` and
  the payload is nested under `data`, so `WorktreeError` has a `Data` object:
  `worktree.Data.Message`, `worktree.Data.ForceRequired`.

Both implement `IOpenCodeError` and both expose the discriminator as `Tag`, so a `switch` on the
type never has to care which dialect produced it. Only the *shape inside* differs, and it differs
because the server's does.

### The unknown-error carrier

A server newer than this SDK's pinned snapshot can send an error tag this build has never seen.
That is not a parse failure: it arrives as `UnknownOpenCodeError`, carrying the raw `Tag` and the
untouched body as a `JsonElement`. Your `switch` gets a case it can log, report, or even handle
before the SDK is regenerated — and the `default` arm above catches any typed error you did not
write a case for.

`Error` can also legitimately be `null`: the server answered with a failure status but no body the
SDK could type. `RawBody` still has the bytes.

## 🔍 Guarded payload accessors

Payload members on a response are guarded. Reading one on an error response throws
`InvalidOperationException` — "The response is an error; check IsError before accessing Sessions."
— rather than handing you a fabricated empty value:

```csharp
var page = await sessions.ListSessionsAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

// Reading Sessions on an error response throws InvalidOperationException; IsError is the guard.
var count = page.IsError ? 0 : page.Sessions.Count;
```

That is the whole contract: **check `IsError` first**. With the default throwing behaviour you
never meet the guard at all, because a failure never reaches your hands as a response. It exists
for `NoThrow`, where it turns "I forgot to check" into an immediate, obvious error instead of a
silent zero-length list.

## 🔌 Transport failures are a different plane

`OpenCodeTransportException` means the call never produced a usable response at all: a connection
that failed, a body that could not be decoded, an undeclared redirect, a JSON payload that did not
match its declared schema, a read that stalled past the internal progress window.

```csharp
try
{
    var health = await client.GetHealthAsync(OpenCodeRequestOptions.NoThrow);

    Console.WriteLine(health.Status);
}
catch (OpenCodeTransportException transport)
{
    Console.WriteLine($"the call never produced a response: {transport.Message}");
}
```

Note the `NoThrow` in that snippet — it is not a contradiction. `NoThrow` turns *declared API
errors* into data; a transport failure was never an API answer, so it still throws. If your code
must not blow up, catch `OpenCodeTransportException` even when you are using `NoThrow` everywhere.

`OperationCanceledException` is never repackaged: your cancellation stays your cancellation.

## 🚀 When the launcher fails

`OpenCodeServer.StartAsync` has its own failure type, `OpenCodeServerException`:

```csharp
try
{
    await using var server = await OpenCodeServer.StartAsync();

    Console.WriteLine(server.Endpoint);
}
catch (OpenCodeServerException failure)
{
    Console.WriteLine($"the server never came up: {failure.Message}");
}
```

Whenever a child process actually ran, the message carries a bounded tail of its **stderr** — which
is usually the answer. The four causes are an exit before readiness (naming the exit code), a
readiness timeout (naming the bound you configured), a first stdout line that was not the readiness
contract (quoting it), and a spawn failure. Only the last carries no stderr, for the good reason
that nothing ran.
