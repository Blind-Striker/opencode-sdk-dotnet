# Client Runtime Architecture

Date: 2026-08-29

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

- The normal PTY family is one of the two families whose **public** surface is hand-written:
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

### PTY WebSocket session

- `PtyClient.ConnectAsync` opens `PtySession`, the family's live working object: `ReadAsync`
  enumerates `PtyFrame` values, `WriteAsync` sends input, and `DisposeAsync` closes. The session
  owns its socket, so disposing it is the only way to end the connection.
- **Transport divergence.** This is the one SDK door that does not ride the HTTP pipeline. The
  upgrade builds its own `ClientWebSocket`, so a caller-supplied `HttpClient`, its proxy, its
  handler chain, the redirect policy, the pooled-connection lifetime, and the pipeline's progress
  window **do not apply** to a PTY session. What the session does inherit is the construction-time
  `ConnectionSnapshot` the pipeline publishes: the normalized endpoint, the Basic credential, and
  the ambient location.
- **Authentication.** The Basic credential rides the upgrade request's `Authorization` header. The
  API's authentication middleware skips credentials only for a URL carrying a non-empty `ticket`
  query, so a header-authenticated upgrade is the designed non-browser path. The SDK never mints a
  ticket for its own connection — a single-use 60-second credential in a URL that reaches logs is
  strictly worse than the header the client already holds. `CreateConnectTokenAsync` stays the
  public door for handing a browser one, and `PtyConnectOptions` deliberately has no ticket member.
- **Address.** `http`/`https` become `ws`/`wss`; the path is `/api/pty/{ptyID}/connect`. The query
  carries the merged location as `location[directory]`/`location[workspace]` plus `cursor` when
  set, built through the same `QueryStringBuilder` every generated route uses. The connect scope
  must resolve identically to the scope the token door resolved.
- **Location merge.** `PtyConnectOptions.Location` merges over the ambient location member by
  member, with exactly the sealed semantics `RequestDecorationPolicy` applies on the header
  channel: per-call wins, null inherits, no clearing. `LocationMerge` states that rule once for
  the query channel; the policy keeps the equivalent fused form so the ambient directory's escape
  stays computed once.
- **Cursor.** Omitted replays the full retained buffer, `-1` attaches live-only, and a value at or
  above zero resumes from that absolute output cursor. The server accepts only JavaScript safe
  integers at or above `-1` and silently coerces anything else to omitted, so `PtyConnectOptions`
  refuses an out-of-range value rather than letting a resume become a full replay.
- **Failed upgrade.** A missing PTY answers plain HTTP 404 before upgrading; a rejected credential
  or origin answers 401/403. A failed upgrade has no response spine, so it cannot ride ADR-0007's
  envelope machinery: the transport plane is the honest channel and every case throws
  `OpenCodeTransportException` naming the PTY and the cause. Modern targets read the status from
  `ClientWebSocket.HttpStatusCode` (enabled by `CollectHttpResponseDetails`); `net472` and
  `netstandard2.0` cannot report it, so the failure names the connect context instead of guessing
  a status. `platform-and-packaging.md` owns the target-framework detail.
- **Frames.** Server output rides text frames and decodes as `PtyOutputFrame`. The one binary
  control frame is a `0x00` marker byte followed by UTF-8 JSON `{"cursor": n}`, sent once after
  replay, and decodes as `PtyCursorFrame`; a control body that is not a JSON object carrying an
  integer `cursor` is a protocol failure. A binary message that does not start with the marker is
  ordinary output. Output is decoded with **replacement**, never fatally: the server chunks its
  replay at 64Ki UTF-16 code units, so a chunk boundary can split a surrogate pair.
- **Input.** A terminal's Enter key is carriage return (`\r`); `WriteAsync` sends exactly the
  bytes it is given, so a caller submitting a command must end the line with `\r` — `\n` renders
  the text but never submits it (research log Q151).
- **Close.** 1000 ends the enumeration normally — the process exit code is not on this wire, so a
  reader that needs it calls `GetPtyAsync`. 4404 means the session was not found or had already
  exited and throws with the reason; because an exited PTY still upgrades cleanly, that failure
  surfaces on the first read rather than on connect. Any other close is an abnormal close, and a
  socket fault maps through `FailureClassification`'s PTY WebSocket phases.
- **Concurrency and disposal.** One session carries one active read enumeration — message
  reassembly cannot be shared — and a second concurrent enumeration is refused with
  `InvalidOperationException`. Sends are serialized behind a semaphore because the socket allows
  one outstanding send. Caller cancellation stays `OperationCanceledException`. Disposal closes
  gracefully under a bounded wait, then tears the socket down; it is idempotent, and a dispose
  racing a pending read completes that read as a normal end rather than a fault.

### Persistent PTY family

- The persistent PTY family carries the same ownership: `PersistentPtysClient` and
  `PersistentPtyClient` live in `src/OpenCode.Sdk/PersistentPtys/` as hand-written code over the
  generated `PersistentPtysRawClient`/`PersistentPtyRawClient` beside them, under the same
  `internalRaw` curation emission, with routes, adapters, verdicts, wire models, envelopes, and
  serializer metadata all still generated (ADR-0021). Placement follows ADR-0019 over the group's
  `ptyID` handle parameter: the id-keyed operations sit on the bound handle, while the
  session-keyed `list`, `create`, and `read` and the unkeyed `handoff` and `shutdown` sit on the
  collection client with their route values as arguments, exactly as upstream flattens the group.
- **The doors.** `PersistentPtysClient` carries `ListPersistentPtysAsync(sessionId)`,
  `CreatePersistentPtyAsync(sessionId, request)`, `ReadAsync(sessionId, …)`, `HandoffAsync`,
  `ShutdownAsync`, and `GetPersistentPtyClient(ptyId)`; the handle carries `GetPersistentPtyAsync`,
  `UpdatePersistentPtyAsync`, `RemovePersistentPtyAsync`, `GetSnapshotAsync`,
  `CreateConnectTokenAsync`, and `ConnectAsync`. `HandoffAsync` and `ShutdownAsync` are
  server-lifecycle doors rather than terminal doors: the first prepares the daemon to outlive this
  server until a replacement claims it, the second stops the daemon and every terminal it owns.
  `CreateConnectTokenAsync` applies the `x-opencode-ticket` sentinel through the internal
  `PtyTicketHeader` both families share, since both connect-token handlers require the same value.
- **Connect and attach.** `PersistentPtyClient.ConnectAsync` opens `PersistentPtySession` and
  returns only after the server's `attached` frame, so `PersistentPtySession.Attachment` is always
  known: the attachment identity, the negotiated input protocol, the terminal as it stood, the
  granted role — which is not necessarily the role the request asked for — the resize generation,
  and the replay bounds. That frame is consumed at connect and never yielded to a read, and a
  server negotiating any input protocol but the framed one is a connect-time protocol failure. The
  upgrade is the same transport divergence the normal family's is; the address is
  `/api/experimental/persistent-pty/{ptyID}/connect` and its query carries no location, because
  this family's terminals are keyed by id alone.
- **Bytes, not text.** Output rides binary messages, so `PersistentPtyOutputFrame.Data` is
  `ReadOnlyMemory<byte>` and nothing is decoded: a frame is free to split a multi-byte character,
  and a caller feeding an emulator writes the bytes as they are. A screen checkpoint — the
  terminal-escape stream that repaints a screen state — is bytes for the same reason, on
  `PersistentPtySnapshot.Checkpoint` and on the resize frame alike: base64 on the wire,
  `ReadOnlyMemory<byte>` in the SDK.
- **Frames.** `ReadAsync` yields a closed `PersistentPtyFrame` hierarchy — one named
  `PersistentPty*Frame` type each for the attached, output, replay-complete, resized, exited,
  controller-changed, title-changed, and foreground-process-changed messages — plus
  `PersistentPtyUnknownFrame`, which carries a control `type` this SDK does not know together with
  its raw body instead of failing the read, because the socket is an experimental surface that may
  grow kinds. A body that is not a JSON object
  carrying a string `type`, and a known frame whose members cannot be read, are both protocol
  failures, reported apart because they mean different things.
- **Input.** `WriteAsync(ReadOnlyMemory<byte>)` and `ResizeAsync(cols, rows)` each send one binary
  message in the framed input protocol's layout — `[type u8][cols u16 BE][rows u16 BE][data]`,
  type 1 for input and type 0 for a viewport change — which the SDK negotiates on every connection
  and is the only protocol it writes. The viewport is a wire fact, not a caller preference: the
  session starts it at the attachment's size and follows every resize frame the read enumeration
  yields, whoever caused it, so a later write carries the size the server believes. Input from a
  connection the server attached as an observer is accepted here and dropped there.
- **Cursor.** `PersistentPtyConnectOptions.Cursor` is a relay to the connect query and nothing
  more. There is no live-only mode here: null replays from the oldest retained byte, and zero means
  the same rather than "replay nothing". The server accepts JavaScript safe integers at or above
  zero and answers HTTP 400 before the upgrade for anything else, so the option refuses an
  out-of-range value rather than spending a round trip on it. Per-frame offsets are not on this
  wire, so a resume anchors on the previous connection's replay-complete `EndOffset` or on the
  terminal's `Info.Output.Tail`; a cursor pointing at trimmed output is advanced by the server,
  which reports the gap through the attachment's replay bounds.
- **Close.** 1000 ends the enumeration normally. 4404 means the terminal does not exist **or** the
  `opencode-pty` daemon is unavailable — one application code for both causes, which a caller
  cannot tell apart from the wire — and because this family runs no pre-upgrade existence check it
  arrives before the `attached` frame, so `ConnectAsync` surfaces it rather than the first read.
  Any other close is abnormal.
- **Failed upgrade.** The connect query and the credential are checked before the upgrade: 400
  names the rejected query (the cursor is the value that can produce it), 401 and 403 name the
  rejected credential or origin, and any other status is named as the answer it was. There is
  deliberately no 404 arm even though the pinned document declares one — a missing terminal
  upgrades and then closes 4404.
- **Daemon facts a caller must know.** These terminals belong to the `opencode-pty` daemon rather
  than to the server process: the server spawns it as its own child, and the terminals survive a
  server restart through `handoff`. At the accepted pin the daemon ships darwin and linux platform
  packages only, so on any other platform `create` — the one route that starts it — answers the
  declared 503 whose `service` is `opencode-pty`, while the rest take their daemon-absent arms:
  `list` an empty list, `read` a null payload, the id-keyed reads and writes a 404 they share with
  an unknown id, `shutdown` a 204, and `connect` a 4404 close. `ShutdownAsync` ends every terminal
  the daemon owns, not one.
- **Shared core.** Both families' sessions run on the internal, family-neutral
  `TerminalSocketCore<TFrame>` — receive with fragment reassembly, serialized sends, a bounded
  graceful close, idempotent disposal, and one active read enumeration — and differ only behind
  three named seams: `ITerminalFrameDecoder<TFrame>` for what a message carries,
  `ITerminalClosePolicy` for what a close status means, and `ITerminalUpgradeFailurePolicy` for
  what a refused upgrade means.

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

`OpenCodeServer.StartAsync(OpenCodeServerOptions?, CancellationToken)` is the standalone door
(upstream `Standalone.start` parity), hand-written over `System.Diagnostics.Process` with no
process-management dependency (ADR-0001). Every call is always a fresh private server on port
zero: the caller's `Command` plus `--stdio --port 0` is the argv, and a freshly generated lease
credential is injected into the child environment as `OPENCODE_PASSWORD`, after any caller-supplied
`Environment` entries so it can never be shadowed. Readiness is the single JSON stdout line the
child prints once fully booted; stdin stays open as the ownership lease for as long as the server
runs, and every later stdout line plus all of stderr is drained continuously (stderr into a bounded
tail kept for failure diagnostics) so a chatty child can never wedge the pipes.

Disposal is a ladder, bounded at every step so it never hangs the caller: stdin EOF (the lease
release) first, then the configured grace (`GracefulShutdownTimeout`, default 3 seconds — the
reference client's own force-kill window), then a forced whole-tree kill
(`Process.Kill(entireProcessTree: true)` on modern TFMs, `taskkill /pid … /T /F` on downlevel
Windows, plain `Kill()` on downlevel non-Windows once the stdin-EOF lease has already released any
children), then a final bounded forced-exit wait. Ownership is structural: the returned
`OpenCodeServer` is the only owner of its child, disposal ends exactly that child, and the
operating system closes the lease even when the owner crashes before disposal runs — coexistence
with any other running server is safe by construction, since a started door never discovers or
attaches to one.

`CreateClient(Action<OpenCodeClientOptions>?)` pins the connection identity fail-closed: the
delegate receives a fresh identity-unset options instance, and setting `Endpoint`, `Username`, or
`Password` there is refused with `InvalidOperationException`. On success the door never mutates the
delegate's instance: it builds a distinct, freshly constructed `OpenCodeClientOptions` carrying its
own endpoint and lease credential, copying over only the behavior members (`Location`) the delegate
set; the object the delegate received stays identity-unset for as long as the caller keeps a
reference to it. Every call builds a new client over its own owned transport, which the caller
disposes.

The failure plane is `OpenCodeServerException : OpenCodeException`. A bounded stderr tail rides
every startup failure that reaches a running child: an exit before readiness (naming the exit
code), a readiness timeout (naming the configured bound), and a non-contract first stdout line
(quoting it) all carry it. A spawn failure (wrapping the underlying `Win32Exception`) carries none
— nothing ran, so there is no stderr to report. Caller cancellation during the readiness wait stays
`OperationCanceledException` rather than being folded into the exception type, after the child is
torn down.

Launcher acceptance is real-process and three-OS. Platform-specific behavior is tested on the
platform it represents; a successful compile is not a lifecycle proof.

## Connection modes

The SDK targets all three upstream connection modes, named after upstream's own verbs — no
invented method names, and every variation rides options arguments rather than a new door.

**Standalone start** (`Standalone.start` parity, upstream `packages/cli/src/services/standalone.ts`;
CLI `--standalone`) → `OpenCodeServer.StartAsync`, above: always a fresh private server on port
zero with its own generated lease credential, never discovering or attaching to another server, so
coexistence with any running server is safe by construction. The returned working object is the
only owner of its child. This door has landed.

**Explicit endpoint** (CLI `--server` parity; upstream builds a plain client plus a 5-second-bounded
health check and a version warning, `server-connection.ts:24-39`) → plain `OpenCodeClient`
construction. There is no dedicated SDK verb or member for this door; the validation recipe
composes two existing pieces: construct the client against the known endpoint, call
`GetHealthAsync` under a caller-owned `CancellationTokenSource(TimeSpan.FromSeconds(5))`, and
compare the returned `Health.Version` against the caller's own expectation. The SDK carries no
version comparand of its own — the accepted snapshot (`spec/SNAPSHOT.md`) is a protocol identity,
not a runtime version — and the network-timeout knob a first-class helper would want is
M6-deferred, so **no new public member lands for this door in this arc**: a dedicated helper would
need a version comparand the SDK does not have and would pre-empt a timeout channel that does not
exist yet, and the generated client cannot gain members in this arc's territory. The recipe is
documented here and demonstrated by the sandbox's `StandaloneServerWalkthrough`, which runs the same
tail without `StartAsync` once a caller already holds an endpoint. *Noted for later:* revisit once
the M6 network-timeout knob lands as an option rather than a caller-owned
`CancellationTokenSource` — a bounded-probe helper only earns public surface at that point.

**Background service** (`Service.discover/ensure/stop`, public export `@opencode-ai/client/service`,
upstream `packages/client/src/promise/service.ts:255`) → the queued follow-up arc; the SDK has no
`DiscoverAsync`/`EnsureAsync`/`StopAsync` parity yet. See `docs/ROADMAP.md` §4 for status.
