# opencode SDK for .NET

Typed .NET client for the opencode HTTP API, plus an MCP server built on it. The domain is
largely upstream's — opencode's concepts seen through an SDK lens — with a few terms this
project coins for itself.

## Language

### Upstream domain (as visible at the API surface)

**Session**:
A durable conversation with an agent; the API's central concept.

**Message**:
One entry in a Session's history (user or assistant), composed of Parts.

**Part**:
A typed fragment of a Message (text, tool, file, reasoning, …); a discriminated union keyed by
a literal `type` marker.

**Event**:
Something that happened, delivered over SSE.

**Durable Session event stream**:
The per-Session event stream; callers request continuation through the `after` cursor. Persistence,
retention, and replay guarantees remain unestablished.
_Avoid_: session events (ambiguous with the live stream)

**Live event stream**:
The instance-wide event stream; the pinned operation has no filter, cursor, replay, or resume
channel. Consumers refresh authoritative state and resubscribe after a disconnect.

**Permission**:
The user-approval gate for agent actions, surfaced as permission requests answered through the
API.

**Provider**:
An LLM provider entry in the catalog, carrying its Models.

**Model**:
One model offered by a Provider.

**Agent**:
A configured opencode working mode/persona (build, plan, …) selectable per Session.

**Server process**:
One running `opencode serve` process (one endpoint); the API is bound to a single Server
process, and cross-process aggregation lives above the SDK. Owns process-global state (auth
store, global config, the global event stream).

**Instance**:
One project-directory context inside a Server process, selected per request via Directory
targeting; a Server process hosts many.

**Directory targeting**:
Per-request project targeting via `location[...]` query parameters, with the
`x-opencode-directory`/`x-opencode-workspace` headers as the ambient channel the server resolves
per member after any query value.

**PTY**:
A pseudo-terminal session managed through the API.

**PTY connection**:
The live WebSocket attached to one PTY, carrying replayed and live output out and input in.
Distinct from the PTY itself, which outlives any connection to it.

**Replay cursor**:
The absolute position in a PTY's retained output. Normal PTY: a connection omitting it replays the
whole retained buffer, `-1` attaches live-only, and a value at or above zero resumes from there.
Persistent PTY: omitted means 0 (the oldest retained byte), there is no live-only mode, and the
resume anchors are `replay_complete.endOffset` and `Info.Output.Tail`.

**Connect ticket**:
The short-lived, single-use credential the token door mints for handing a PTY connection to a
browser. The SDK never mints one for its own connection.

**Persistent PTY**:
A terminal owned by the `opencode-pty` daemon rather than by the Server process; it survives a
server restart through a handoff, is keyed to a Session, and is the source of `read`'s "current
terminal".

**Attachment**:
One live connection to a Persistent PTY: its identity, its controller/observer role, the resize
generation it attached at, and the replay bounds the server granted it.

**Controller / Observer**:
The two roles a Persistent PTY grants an Attachment. A Controller writes input and resizes; an
Observer reads output only, and its input is dropped server-side. The server grants the role, so
it is not necessarily the one the connection asked for.

**Checkpoint**:
The terminal-escape byte stream that repaints a screen state, carried base64 on the wire and as
bytes in the SDK.

**Framed input**:
The `input_protocol=1` layout a Persistent PTY connection writes: `[type][cols][rows][data]`, so
every input or control message carries the viewport it was typed at.

### This project's language

**Protocol surface** (historically "modern surface" in dated research docs):
The `v2.*`-prefixed protocol operation block — the surface this SDK generates (ADR-0005);
public names carry no prefix.
_Avoid_: v2, V2 (in public naming); legacy (the retired 1.x dual-surface vocabulary)

**Launcher**:
The in-core component that starts, monitors, and stops a local `opencode serve` process —
`OpenCodeServer.StartAsync` and its working object, covering the standalone-server connection
mode. Discovery/attachment of background services is deliberately not the launcher's job.

**Standalone server**:
A fresh private `opencode serve --stdio --port 0` child owned by the SDK caller through
`OpenCodeServer`: generated lease credential, stdin-EOF ownership, bounded tree termination.
Upstream's `Standalone.start` connection mode.

**Background service**:
Upstream's registered daemon connection mode (`Service.discover/ensure/stop` over a
registration file); the SDK's `DiscoverAsync`/`EnsureAsync`/`StopAsync` parity is a queued
follow-up arc, not part of M4.

**Registration file**:
The on-disk record a background service publishes (address, credential, instance identity) so
clients can discover it; an upstream-observed contract outside the OpenAPI pin.

**Accepted snapshot**:
The reviewed protocol identity the SDK builds against: an exact upstream commit, the committed
`spec/openapi.json` digest, an ordered snapshot recipe, a sorted operation-set digest, and the
matching submodule gitlink. Provenance in `spec/SNAPSHOT.md`.
_Avoid_: spec pin (retired term)

**Snapshot recipe**:
The ordered procedure producing the accepted document from the exact upstream commit; its patch
list is normally empty, making production an identity transform.

**Snapshot receipt**:
The immutable record of one prepared snapshot candidate — inputs, hashes, patches, invariants —
reviewed by a human before it is applied.

**Restore patch**:
A temporary, hash-verified snapshot-production patch recovering contract content upstream's
projection lost; carries an upstream report and a retirement predicate.

**Source watch**:
The pinned set of upstream files a hand-written door reads as inputs — each a watched source
recorded by path, SHA-256 blob hash, and one content anchor in `spec/source-watch.json` and the
receipt's `watchedSources`. A refresh-time review trigger only; it never reaches ingestion,
curation, or emission.

**Contract inventory**:
The complete operation set of the accepted document, each operation carrying its admission state
(selected, pending, or transport-owned).

**Target surface**:
The operations required to be callable; defaults to the complete contract inventory.

**Transport-owned operation**:
An operation whose transport the HTTP pipeline cannot carry (a WebSocket upgrade);
generator-owned as inventory and exclusion fingerprint, callable only through a hand-written
door.

**Operation-identity row**:
A reason-bearing curation row admitting and naming an operation whose upstream identity violates
upstream's own conventions; carries the upstream report and retires when the fix lands.

**Model layer**:
The generated types and serializer registry shipped inside `OpenCode.Sdk`.

**Envelope**:
The generated, typed per-operation response object carrying status/error state plus named
payload properties.

**Single-key envelope**:
A success body an operation declares inline as an object requiring exactly one property that is
not `data` (`SpecEnvelopeShape.SingleKey`); the payload flattens onto the response under that
key's PascalCase name and the wrapper is never emitted as a model. An inline object whose sole
property is optional has no payload the envelope can promise and refuses at bind time instead.

**Bound handle**:
A sub-client bound to one resource id (e.g. a session) — partial application over the
shared pipeline; never caches server state.

**PTY session** (`PtySession`):
The working object over one PTY connection: read frames, write input, dispose to close. One of the
two doors that build their own transport instead of riding the HTTP pipeline (the persistent PTY
session is the other).

**PTY frame**:
One message read from a PTY connection — either output text or the single cursor control frame
the server sends once the retained buffer has been replayed.

**Terminal socket core**:
The family-neutral WebSocket lifecycle both PTY sessions share — receive and reassembly,
serialized sends, bounded close, disposal — with per-family decode, close, and upgrade-failure
behavior behind named seams.

**Connection snapshot**:
The construction-time endpoint, credential, and ambient location the pipeline publishes for a
door that cannot ride its policies.

**Curation config**:
The generator's declarative, fail-closed input mapping spec constructs to public names and
rules; an unmapped construct breaks generation.

**Stabilize-duplicate collapse**:
The mechanical fold of a reachable `<base>_<N>` component into `<base>` when
`StabilizeDuplicatePolicy` finds the two structurally identical, refusing by name when it does
not; recorded as an implicit alias in `.generated-manifest.json`'s `implicitAliases` section
rather than a curated `schemaAliases` row.

**Unknown variant carrier**:
The per-union `Unknown*` variant absorbing unrecognized discriminators at runtime (tag
string + raw payload).

**Fingerprint pin**:
The committed hash of an excluded operation's full spec subtree; CI breaks when the pinned
construct drifts.

**Output manifest**:
The committed inventory of generated files; generated-ness is tracked here, never by
folder or file-name convention.

**Recorded tolerance**:
An explicit, registered runtime exception to the fail-closed default.

**Behavior core**:
The hand-written transport core where all request behavior lives; generated methods only
delegate to it.
