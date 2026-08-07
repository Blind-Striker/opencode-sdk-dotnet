# MCP 2026-07-28, the MCP C# SDK v2.0, and SSE in .NET

> Research snapshot, 2026-08-08. Sources verified live:
> modelcontextprotocol.io/specification/2026-07-28 (changelog) and the .NET blog post
> "Announcing v2.0 of the official MCP C# SDK".

## MCP spec revision 2026-07-28 — the big one

Described as the largest revision since the protocol launched. What matters for us:

**Major changes:**

- **Stateless protocol.** The `initialize`/`initialized` handshake and the
  `Mcp-Session-Id` header are gone. Every request carries protocol version and client
  capabilities in `_meta`; servers identify themselves in each result's `_meta`.
  New required `server/discover` RPC advertises versions/capabilities.
- **MRTR (Multi Round-Trip Requests)** replaces server-initiated requests
  (`sampling/createMessage`, `elicitation/create`, `roots/list`): the server returns
  `resultType: "input_required"` with `inputRequests`; the client retries the original
  request with `inputResponses`. All results now carry a required `resultType`.
- **`subscriptions/listen`** — one long-lived POST-response stream for opted-in change
  notifications — replaces the HTTP GET endpoint and `resources/subscribe`.
- **Tasks moved out of core** into an official extension
  (`io.modelcontextprotocol/tasks`), redesigned around polling (`tasks/get`) +
  `tasks/update`.
- `ping`, `logging/setLevel`, `notifications/roots/list_changed` removed; SSE
  resumability (`Last-Event-ID`) removed from Streamable HTTP — a broken stream means
  re-issuing the request.
- Cross-call state is explicit: server-minted handles passed as ordinary tool
  arguments (list endpoints no longer vary per connection).

**Deprecated (do not build on):** Roots, Sampling, and Logging features; the old
HTTP+SSE transport; RFC 7591 Dynamic Client Registration (in favor of Client ID
Metadata Documents). Twelve-month deprecation window policy adopted.

**Nice-to-know minors:** required `ttlMs`/`cacheScope` on list/read results (client
caching); deterministic `tools/list` ordering recommended for prompt-cache hits;
OpenTelemetry trace-context conventions in `_meta`; JSON Schema 2020-12 fully allowed
in tool schemas.

## MCP C# SDK v2.0

Implements the 2026-07-28 revision. Package layout:

| Package | Role |
|---|---|
| `ModelContextProtocol.Core` | Client + low-level server, minimal deps |
| `ModelContextProtocol` | Stdio server, hosting/DI, attribute-based discovery |
| `ModelContextProtocol.AspNetCore` | Streamable HTTP server transport |
| `ModelContextProtocol.Extensions.Tasks` | Long-running tools (polling) |
| `ModelContextProtocol.Extensions.Apps` | Server-delivered UI (experimental) |

- Targets **net8.0 / net9.0 / net10.0 + netstandard2.0**.
- HTTP transport is **stateless by default** (horizontal scaling / edge friendly).
- v1 APIs keep compiling in 2.0 with deprecation warnings (MCP9004–9006) — migration
  is guided, not forced.
- Typical server: `AddMcpServer().WithHttpTransport().WithToolsFromAssembly()` +
  `app.MapMcp()`.

## Roadmap implications (SDK first, then MCP server) — confirmed

1. The MCP server becomes a **thin adapter over our own SDK**: each MCP tool ≈ one SDK
   call + formatting. (The unofficial `opencode-mcp` proves the shape and the failure
   mode — see doc 03.)
2. **Stateless MCP fits opencode naturally:** opencode already does per-request project
   targeting via the `x-opencode-directory` header, so no protocol-level session state
   is needed.
3. We can offer **both stdio and streamable HTTP** transports from day one (the
   unofficial server is stdio-only).
4. Don't invest in deprecated features (Sampling/Roots/Logging, HTTP+SSE transport).

## SSE — and how to expose it in .NET

**What SSE is:** one-way server→client event streaming over a single long-lived HTTP
response (`Content-Type: text/event-stream`, line-based `data:` events). Unlike
WebSockets: unidirectional, no protocol upgrade, proxy/load-balancer friendly.
opencode's `/event`, `/global/event`, `/api/event`, `/api/session/{id}/event` all use
it.

**.NET support is first-class:**

- **Client:** `System.Net.ServerSentEvents` (`SseParser.Create(stream)`,
  `EnumerateAsync()`) — in-box since .NET 9, available as a NuGet package down to
  netstandard2.0. Combine with `HttpClient` +
  `HttpCompletionOption.ResponseHeadersRead`.
- **Server (later, for our MCP server / any UI):** ASP.NET Core has
  `TypedResults.ServerSentEvents` in .NET 10.

**Design decisions for our SDK (mirroring upstream — see doc 02):**

- Expose streams as `IAsyncEnumerable<TEvent>`; the connection opens on first
  `MoveNextAsync`; `CancellationToken` cancels/closes.
- **No automatic reconnect.** Upstream deliberately fails streams on transport loss —
  live events missed during a disconnect cannot be replayed, so consumers must refresh
  authoritative state and resubscribe. For the durable per-session stream, resume is
  explicit via the `after` sequence cursor.
- The event payload is a large discriminated union (part of the spec's 472 schemas) —
  the typed event model (polymorphic deserialization + unknown-event forward
  compatibility) is a deep-dive item in GOAL.md.

## Side note: building UIs on the SDK

A separate UI (or an "opencode HQ" dashboard) over the SDK is exactly what the
architecture supports — every existing front-end is an API client. One structural
caveat: the API is bound to a single opencode instance, and upstream explicitly does
not expose server-global event aggregation. A fleet dashboard means one client + one
SSE subscription per instance, with aggregation built above the SDK. Single-server
multi-project is easy (`x-opencode-directory`). Auth is HTTP basic
(`OPENCODE_SERVER_PASSWORD`) — anything internet-facing needs its own auth layer in
front.
