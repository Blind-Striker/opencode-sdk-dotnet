# Client Runtime Architecture

Date: 2026-08-20

Canonical current rules for client construction, transport ownership, API errors, streams, and the
local server launcher. Protocol and generated-model rules live in
`protocol-and-generation.md`.

## Construction and transport ownership

- `OpenCodeClient(OpenCodeClientOptions)` is the only public construction path (ADR-0010).
- The SDK owns a singleton-friendly transport and disables automatic redirects on every owned
  handler. A surfaced 3xx is an undeclared protocol response, never a route, authority, method, or
  credential transition the SDK follows implicitly. Modern targets use `SocketsHttpHandler` with a
  120-second pooled connection lifetime. Downlevel targets configure the endpoint-and-proxy
  `ServicePoint` with an effectively unbounded connection limit and the same 120-second connection
  lease before construction and each owned send, avoiding both long-lived stream starvation and
  stale endpoint retention without mutating process-global defaults. The endpoint-scoped
  `ServicePoint` itself is process-shared, so this policy also governs other process traffic to the
  same endpoint-and-proxy pair (ADR-0010).
- The `(HttpClient, options)` constructor is internal friend-assembly surface for this repository's
  tests and benchmarks. There is no public transport-injection constructor (ADR-0010).
- `OpenCode.Sdk.Extensions` registers one singleton root client and resolves every sub-client from
  that instance. It does not use `IHttpClientFactory` or depend on
  `Microsoft.Extensions.Http` (ADR-0010).
- No consumer transport-composition seam is public today. Adding one requires a concrete consumer
  need and a deliberate design; omission is reversible because a future public seam is additive
  (ADR-0010).
- Client options are snapshotted at construction. The SDK does not read process environment
  variables to discover endpoint or credentials; consumers resolve their own configuration.

### Location

- `OpenCodeClientOptions.Location` is the ambient location, snapshotted at construction into a
  precomputed `x-opencode-directory` / `x-opencode-workspace` header pair — the fast path for
  every call that does not override it.
- `OpenCodeRequestOptions.Location` is the per-call override. `RequestDecorationPolicy` merges
  it over the ambient snapshot **member by member**: a set `Directory` or `Workspace` always
  wins over its ambient counterpart; an unset (`null`) member inherits the ambient value
  unchanged. There is no client-side concept of "both locations set the same member" producing
  anything other than the per-call value — per-call always wins when set.
- Because `LocationSelector` refuses blank (`""`/whitespace) members at construction, `null` is
  the only spelling of "leave this member alone." There is no way to clear an ambient member for
  one call — only to leave it inherited or replace it with a different non-blank value.
- **Encoding asymmetry**: the directory member is percent-encoded with `Uri.EscapeDataString`
  before it rides the header (the server percent-decodes it), while the workspace member rides
  the header verbatim (the server reads it as-is). This asymmetry applies identically to the
  ambient snapshot and to a per-call override — encoding is a property of which member is being
  sent, not of which channel set it.
- **Uniform injection, not a query channel.** Both the ambient and the per-call location travel
  on the same `x-opencode-directory` / `x-opencode-workspace` header pair; the SDK performs the
  member-by-member merge itself before sending. This is a deliberate simplification, not the
  per-operation `location[directory]` / `location[workspace]` query-string channel some
  operations declare independently (`QueryStringBuilder.AddLocation`). The two channels are
  unrelated: an operation that accepts an explicit `location` query parameter is unaffected by
  this header merge.
- **Session-route no-op.** The server honors these headers only on the operations whose group
  resolves location from the request; operations that resolve location from a session instead
  (or that do not resolve it at all) ignore both headers server-side. Sending a per-call location
  to a session route is therefore a harmless no-op, not an error — the SDK does not attempt to
  suppress or validate it per route.

## PTY family ownership

- The normal PTY family is the one family whose **public** surface is hand-written:
  `PtysClient` and `PtyClient` live in `src/OpenCode.Sdk/Ptys/` as ordinary hand-written code,
  and the generator emits `PtysRawClient`/`PtyRawClient` internally beside them under the
  `internalRaw` curation emission. Everything else the family needs — routes, query shapers,
  response adapters, status verdicts, wire models, envelopes, and serializer metadata — stays
  generated, so route, status, and schema drift still breaks compilation locally (ADR-0021).
- The hand-written doors keep the generated family's shape: the protected mocking constructor,
  `virtual` members, and the `MockSeam` guard. Each door delegates once into its raw twin and
  adds nothing but the knowledge generation may not import.
- `CreateConnectTokenAsync` is that knowledge. The server's connect-token handler requires a
  fixed `x-opencode-ticket` value that exists only in upstream implementation source, which
  ADR-0013 forbids importing into generation; the constant therefore lives in the hand-written
  door alone and is never a caller's argument, never in curation, and never in generated output.
  The request's `location` query — not the ambient header pair — fixes the scope the ticket is
  minted for.
- **Declared headers are the runtime channel that carries it.** An operation's document-declared
  header parameters ride `PipelineMessage.DeclaredHeaders` (`IReadOnlyList<DeclaredHeader>?`),
  written by `Pipeline` from the value the generated raw method collected and read by
  `RequestDecorationPolicy`, which adds each entry with `TryAddWithoutValidation` exactly as it
  adds the location pair. The policy never learns a family or a header name, so no operation's
  knowledge leaks into it. This is not a general header facility: the channel is assembly-internal,
  only generated internal-raw methods feed it, and only a parameter the pinned document declares
  ever becomes an entry.

## Error channels

- API failures throw through `OpenCodeException` -> `OpenCodeApiException` by default. Tagged
  protocol error payloads remain generated, typed data on the exception (ADR-0007).
- A one-shot call may select per-call `NoThrow`, returning the same typed error data on its response
  spine. There is no client-level error-behavior switch (ADR-0007).
- Throwing API errors retain the raw response body. `NoThrow` responses retain it on the shared
  response spine, including when typed error parsing fails (ADR-0007).
- Transport, status/framing, JSON, dispatch, cancellation wrapping, and impossible top-level
  materialization failures throw `OpenCodeTransportException`. `NoThrow` applies only to declared
  API errors and never suppresses transport failures. Undeclared 3xx responses are protocol
  failures on both one-shot and streaming paths (ADR-0007, ADR-0014).
- Streaming operations return a stream rather than a response envelope and therefore expose no
  per-call request-options parameter. Their failures always throw (ADR-0007).
- Network progress is bounded by an internal per-read window (100 s today): the send and every
  buffered body read must progress inside it, and each read that progresses re-arms it, so a
  slow-but-flowing body survives while a stalled one fails. The pipeline owns this timer — the
  owned transport's `HttpClient.Timeout` is infinite so two mechanisms cannot race, and a
  caller-supplied client's own timeout bounds only its send. A stalled read no token can reach is
  interrupted by disposing the content. An error body returned while opening a stream buffers
  under the same window; a successful SSE body remains live until caller cancellation, server
  completion, or failure. Caller cancellation still passes through; an exhausted progress window
  maps to `OpenCodeTransportException`.
  Successful JSON bodies materialize directly from validated UTF-8 bytes when possible. One decoding
  policy applies the modern `HttpContent` charset/BOM and malformed-UTF-8 replacement algorithm on
  every target framework and on both success and error planes. Error bodies stay decoded strings so
  throwing and `NoThrow` paths retain exact `RawBody`. Declared no-content successes drain an
  unexpected body with the buffered response and ignore it (ADR-0007, ADR-0014).
- A schema-valid reserved failure frame throws `OpenCodeStreamFailureException`, a subtype of
  `OpenCodeTransportException`, with typed causes on its non-null `Cause` collection. Invalid cause
  JSON, null materialization, and declared-but-impossible cause tags remain base transport/protocol
  failures rather than partially typed exceptions (ADR-0015).

## Server-sent events

- Streams are exposed as `IAsyncEnumerable<T>` and open lazily on first enumeration.
- Cancellation closes the stream through the enumeration token. On downlevel targets, the SDK also
  disposes the live response so cancellation interrupts platform response-stream reads that do not
  observe an async read token; disposal-induced I/O failures remain caller cancellation.
- The SDK never auto-reconnects. A live-stream consumer refreshes authoritative state and
  resubscribes after failure. Durable continuation is requested explicitly through
  `v2.session.log`'s `after` parameter; persistence, retention, and replay guarantees remain
  unestablished (research doc 02, `docs/ROADMAP.md`).
- The SSE event name is a framing signal. An ordinary payload uses the default `message` name;
  the operation's declared failure event materializes its cause through generated metadata and
  throws; any other explicit name is refused. Unknown payload and cause discriminators remain
  governed by ADR-0009 and are not confused with unknown frame names. A known tag whose schema is
  impossible is a protocol failure, not an unknown variant (ADR-0015).
- A body cut in the middle of an event is reported as a transport failure rather than dispatched
  as malformed payload data.

## Pagination

- A supported cursor-list operation has two generated doors: `List*Async` returns one endpoint-
  specific page envelope, while `Enumerate*Async` lazily yields its items across pages. Explicit
  pages retain cursor/status metadata and per-call `NoThrow`; automatic item traversal has no
  response envelope and therefore always throws API errors when their page is reached (ADR-0007,
  ADR-0017).
- `ListRequest` carries the pinned string `limit`, first-page `order`, and opaque `cursor` channels;
  `ListCursor` preserves the response's optional `previous` and `next` values. The first automatic
  request is sent unchanged, including an order-plus-cursor pair. Each continuation retains the
  initial `limit`, omits `order`, and sends the returned `cursor.next` without decoding it.
- A missing `next` cursor is the only normal end signal. An empty page with `next` continues;
  `previous` remains available through explicit page calls. Cursor values are never normalized,
  incremented, compared, or cycle-checked. Cancellation reaches each request and is checked between
  buffered items.
- Automatic pagination is a finite pull sequence over ordinary buffered HTTP calls, not SSE. A
  different pagination dialect does not inherit these rules from naming or prose; it requires a
  mechanically proven binding of its own (ADR-0013, ADR-0017).

## Launcher

The local `opencode serve` launcher belongs in `OpenCode.Sdk` when its milestone lands. Its design
is hand-written over `System.Diagnostics.Process`: start and monitor one known executable, drain
output safely, and own graceful/forceful shutdown without a process-management dependency
(ADR-0001). `docs/ROADMAP.md` owns its current delivery status.

Launcher acceptance is real-process and three-OS. Platform-specific behavior is tested on the
platform it represents; a successful compile is not a lifecycle proof.
