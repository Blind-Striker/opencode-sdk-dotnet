# sdk-next, the embedded model, and why the HTTP surface is safe to build on

Date: 2026-08-16

> Sources, retrieved 2026-08-08: `external/opencode/packages/sdk-next/README.md`,
> `external/opencode/CONTEXT.md` ("Client contract architecture" and related decisions).
> The streaming section below is re-derived against the v2 wire, retrieved 2026-08-16:
> `effect/unstable/encoding/Sse.ts`, `packages/server/src/handlers/event.ts`,
> `packages/server/src/event-feed.ts`, `packages/server/src/handlers/session.ts`,
> `packages/httpapi-codegen/src/index.ts`.

## The next-generation pipeline (private, in progress)

Three private packages prepare the opencode v2-era SDK story:

| Package | Role |
|---|---|
| `@opencode-ai/httpapi-codegen` | Generator: compiles the Effect `HttpApi` into an **SDK Contract IR**, then emits clients from the IR (`emitPromise`, `emitEffect`). Skips OpenAPI entirely. |
| `@opencode-ai/client` | The generated **network-only** clients. Root export = zero-Effect Promise client over `fetch`; `/effect` export = rich Effect client with decoded branded values. |
| `@opencode-ai/sdk-next` | The **embedded in-process host** (see below). Will take over the `@opencode-ai/sdk` name after legacy consumers migrate. |

So two distinct changes are happening, and it's important not to conflate them:

1. **Generation change:** clients generated from the `HttpApi` contract directly
   instead of via OpenAPI + hey-api.
2. **Execution-model addition:** an embedded mode for same-process JS consumers.

## What "embedded / in-process" actually means

The nearest .NET analogy is **ASP.NET Core's `TestServer` / `WebApplicationFactory`**.

opencode's server is an Effect `HttpRouter` — a data structure mapping
request → middleware → handler → response. Networked mode binds that router to a TCP
listener. Embedded mode invokes **the same router object as an in-memory call** inside
the same JS process. From the README:

> "The SDK executes Server's assembled HTTP router in memory. It opens no listener and
> performs no network I/O, while preserving the same routing, middleware, handlers,
> codecs, and errors as the network client."

What's preserved: routes, middleware (auth/permissions), codecs (encode/decode still
happens), handlers, error types. What's removed: the TCP socket, port management, and
child-process spawning. Extras that only make sense in-process: `tools.register(...)`
(registering local tools), scope-based resource cleanup (closing the Effect Scope
releases DB handles, fibers, registrations).

Who it's for: same-process JS consumers only — the CLI/TUI (which already has an
in-memory `http://opencode.internal` transport case in
`packages/opencode/src/cli/cmd/tui.ts`), the desktop app's JS side, tests, plugins.

**A .NET client can never use embedded mode** (opencode is a JS application; its router
can't be hosted in a CLR process). That's fine — see below.

## The HTTP surface is not going away — upstream's own commitments

Direct quotes from `CONTEXT.md`:

> "Networked and **Embedded OpenCode** use the same **OpenCode Client** and preserve
> the full HTTP encoding, routing, middleware, and decoding boundary; **only the
> `HttpClient` transport differs**."

> "A capability intended for both networked and **Embedded OpenCode** belongs in the
> authoritative public `HttpApi`; embedded-only same-process capabilities extend
> **Embedded OpenCode** separately."

> "**Preserve V2 route paths, operation IDs, codecs, errors, middleware behavior, and
> OpenAPI output** while making this change."

Reading: the `HttpApi` contract becomes *more* central — it is the single authoritative
contract from which both networked and embedded clients derive. Embedded mode is an
optimization that removes the "spawn a child process and HTTP yourself over localhost"
absurdity for JS consumers. Remote servers (`opencode serve`), the web UI, IDE
extensions, and every non-JS client remain networked HTTP.

**Implication for this project:** our target surface (the v2 routes / OpenAPI output)
is the one thing upstream has explicitly promised to preserve.

## Upstream streaming/API design decisions worth mirroring

Also from `CONTEXT.md` — these shape our .NET API design:

- **No auto-reconnect in generated streaming clients.** Streams fail explicitly on
  transport loss; live consumers refresh authoritative state and resubscribe. Durable
  resume is explicit composition above the client — the caller passes `v2.session.log`'s
  `after` cursor; the SSE `id:` line is not a resume token.
- **Two distinct event streams with different guarantees:** `v2.event.subscribe`
  (`/api/event`) = instance-wide, live-only, no replay, no parameters at all;
  `v2.session.log` (`/api/experimental/session/{sessionID}/log`, `after` + `follow`) =
  per-session, durable, replayable via aggregate sequence cursor. A session ID is *not* a
  filter on the live stream — they have different schemas and failure behavior.
- **No server-global event aggregation** in the common client — the API is bound to
  one instance. Fleet dashboards ("opencode HQ") must aggregate above the SDK.
- **Streaming methods return the stream directly** (lazy `AsyncIterable`; connection
  opens on first `next()`, `AbortSignal` cancels). .NET mapping:
  `IAsyncEnumerable<T>` + `CancellationToken`, connection on first `MoveNextAsync`.
- **Pagination:** list endpoints move to an opaque-cursor **Page** discipline —
  continuation accepts only the cursor; filters/ordering are carried inside it.
- **Errors:** Promise clients reject with either a *tagged declared domain failure*
  (structural, with type guards — not exception-subclass identity) or a single
  infrastructure `ClientError` with a structured reason. A clean model to mirror with
  .NET exception design or result types.

## How the v2 SSE wire actually behaves

The spec declares both stream responses with `id`, `event` and `data` all `required`, which
describes the **decoded** envelope rather than the bytes. Effect's parser fills a missing
`event:` line with `"message"` before any value is read, so the decoded shape always has a
name; its encoder is the other half of the same coin and writes `event:` **only when the name
is not `"message"`**, and `id:` only when one is defined. An ordinary payload frame therefore
carries neither line.

That makes `event` a pure signal channel: its presence means the frame is not a payload. The
one signal this API declares is `x-effect-stream.failureEvent:
effect/httpapi/stream/failure`, sitting beside `causeSchema` and `errorSchema`. The reason is
structural — once a 200 and its headers are on the wire, HTTP has no way left to say the
operation failed, so the failed cause is encoded as an in-band frame under a reserved name.
It is the SSE analogue of a trailer.

**The two endpoints do not behave alike despite identical declarations.** `v2.session.log`
is a `.handle(...)` returning a `Stream`, so it runs through the real `HttpApiSchema.StreamSse`
encoder and can emit the failure frame. `v2.event.subscribe` is `handleRaw`: it writes
`data: ${json}\n\n` by hand and keeps the connection alive with `: heartbeat\n\n` comment
lines every 15 seconds, so it never emits `event:` at all and its failures simply close the
connection.

Behaviors of upstream's own generated reader (`httpapi-codegen`), which are worth mirroring
or deliberately not:

| Upstream behavior | Our position |
|---|---|
| Wraps a mid-stream read failure into its infrastructure error type | mirrored — a transport failure stays a transport failure (ADR-0007) |
| Caps a pending event at 16 MiB | mirrored — the same ceiling, counted per pending frame |
| Discards `event:` and `id:`, keeping only `data:` | **not** mirrored; Effect's own parser keeps the name, and discarding it hands a failure frame to the consumer as an ordinary event |
| Strips one leading UTF-8 BOM (Effect's parser; the codegen client does not) | mirrored, following the parser |
| Trims all leading whitespace after `data:` | **not** mirrored; the SSE grammar strips exactly one space |
| Appends `\n\n` at EOF, dispatching a partial trailing line | **not** mirrored; without auto-reconnect a truncated frame is lost data, so it is reported |

**Consumer census (2026-08-16).** `v2.session.log` has exactly one consumer upstream — the
TUI devtools bar, which collects the log with `follow: false` to dump a debug JSON file and
discriminates on `event.type`, skipping the `log.synced` watermark. The web UI, desktop, app,
session-ui and console packages do not call it at all. `v2.event.subscribe` is the one every
front-end uses: 36 call sites in `packages/web`, 2 in the TUI, 2 in the CLI, 1 in the app.
The durable log is a diagnostic channel; the live bus is the product surface.
