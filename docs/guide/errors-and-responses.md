# 🧱 Errors and responses

Date: 2026-08-31

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
SDK could type. `RawBody` then has whatever the server did send — which is sometimes nothing at
all, because a failure answered above the API layer can carry an empty body. The next section is
exactly that case.

### A 401 with no credential

The one failure whose body tells you nothing. An `opencode serve` process **always** runs with a
password — the one you set through `OPENCODE_PASSWORD`, or one it generates and prints as
`server password <pw>` — and it rejects an uncredentialed request before the API layer ever runs.
The answer is a bare `401` with an empty body and a `WWW-Authenticate: Basic` challenge: `Error` is
`null`, `RawBody` is empty, and the typed `UnauthorizedError` the spec declares never arrives.

Because the wire says nothing, the SDK does. When a call answers 401 *and* the client was built
with `Password` left `null`, the exception message carries one extra sentence:

```text
The opencode API returned status 401. The client sent no credential: the opencode CLI always
starts its server with a password, so pass the one it printed as 'server password <pw>', or the
one you set through OPENCODE_PASSWORD, in OpenCodeClientOptions.Password.
```

It is scoped as tightly as it reads: 401 only, and only when no password was configured. A
credential the server *rejected* is a different mistake and keeps the plain message. And it is a
message, not a new member — the `NoThrow` envelope is unchanged, where `Status == 401` on a client
you built without a password is the same signal. Observed on `@opencode/cli@2.0.2`.

### When a worktree remove is refused

`WorktreesClient.RemoveWorktreeAsync` answering 400 `WorktreeError` means **nothing was removed**.
A refusal is not partial cleanup: the directory is still on disk and the worktree is still in the
inventory, so re-list before treating a removal as done.

`Data.ForceRequired` says which kind of refusal it was:

| `ForceRequired` | What it means | What helps |
|---|---|---|
| `true` | The worktree carries changes git will not discard | Retry with `Force = true` |
| `false` | The server ran git and git failed for a reason `Force` cannot fix | Read `Data.Message`; it is git's own stderr |
| `null` | The refusal never reached git | Fix the request — the removal was never attempted |

The `false` arm is the surprising one. Observed on Windows against the then-pinned
`@opencode/cli@0.0.0-beta-19242`: `Data.Message` was git's own
`error: failed to delete '<dir>': Permission denied`, because another process still held a handle
inside the directory while `git worktree remove` tried to unlink it. `Force` unlinks the same file,
so it cannot help. Retry once the holding process has exited — disposing the `OpenCodeServer` you
started is the usual one — or work in a directory your own application owns and remove it yourself.
That is one build's observed behaviour, not a contract: the declared answers stay 204 / 400 / 401.

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
is usually the answer. The causes that reach a running child are an exit before readiness (naming
the exit code), a readiness timeout (naming the bound you configured), and a first stdout line that
was not the readiness contract (quoting it).

One exit before readiness is worth knowing by sight. `opencode` is also the command the 1.x line
installs (npm `opencode-ai`), and a 1.x binary does not accept the `--stdio` flag the launcher
appends: it exits with code 1 at once and prints its own `serve` usage text to stderr, an option
list with no `--stdio` in it. When the stderr tail in the message is that usage text, the `opencode`
on your `PATH` is a 1.x install, which this SDK does not drive — install `@opencode/cli`, or point
`Command` at that install's executable. Measured on `opencode-ai@1.18.30`.

The rest are decided before anything is spawned, so they carry no stderr — there is nothing to
report — but they name exactly what went wrong instead.

**The command was not found.** `Command[0]` was a bare name and no `PATH` directory held it. The
message says how many directories were searched and, on Windows, which extensions were appended:

```text
The server command 'opencode' was not found on PATH: 37 directories searched with the
extensions .COM, .EXE, .BAT, .CMD. Pass the executable's full path in
OpenCodeServerOptions.Command, or install the CLI package @opencode/cli.
```

Those two suggestions are the two real fixes: install the CLI (`npm install -g @opencode/cli`), or
stop depending on the search and name the file — `Command = ["/opt/opencode/bin/opencode", "serve"]`.
The [resolution rules](connection-modes.md#how-the-command-is-resolved) say exactly what was tried.

**An argument was refused.** The command resolved to a Windows `.cmd`/`.bat` shim, which runs
through `cmd.exe`, and one of your leading arguments contained a character `cmd.exe` would re-parse:

```text
The server command resolved to the batch shim 'C:\Users\you\AppData\Roaming\npm\opencode.cmd',
which runs through cmd.exe, and cmd.exe re-parses its command line — so the argument
'serve&whoami' is refused rather than escaped: it contains one of & | < > ^ % ! " or a line
break. Remove the character, or point OpenCodeServerOptions.Command at the real executable
instead of the shim.
```

Refusing beats escaping here (see [BatBadBut / CVE-2024-24576](https://nvd.nist.gov/vuln/detail/CVE-2024-24576)),
so the SDK will not try to quote its way out. Nothing was started when you see this.

**A spawn failure.** The path existed as far as resolution could tell and the operating system
still refused to run it — a wrong architecture, a missing execute bit, a deleted file. The message
names the command you wrote and, when they differ, the path it resolved to.
