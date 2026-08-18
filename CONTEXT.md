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
The instance-wide event stream; no replay guarantee — consumers refresh authoritative state
after a disconnect.

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
Per-request project targeting via `location[...]` query parameters (the v2 convention).

**PTY**:
A pseudo-terminal session managed through the API.

### This project's language

**Protocol surface** (historically "modern surface" in dated research docs):
The `v2.*`-prefixed protocol operation block — the surface this SDK generates (ADR-0005);
public names carry no prefix.
_Avoid_: v2, V2 (in public naming); legacy (the retired 1.x dual-surface vocabulary)

**Launcher**:
The in-core component that starts, monitors, and stops a local `opencode serve` process.

**Spec pin**:
The committed copy of upstream's `openapi.json` under `spec/`, provenance in `SNAPSHOT.md`.

**Model layer**:
The generated types and serializer registry shipped inside `OpenCode.Sdk`.

**Envelope**:
The generated, typed per-operation response object carrying status/error state plus named
payload properties.

**Bound handle**:
A sub-client bound to one resource id (e.g. a session) — partial application over the
shared pipeline; never caches server state.

**Curation config**:
The generator's declarative, fail-closed input mapping spec constructs to public names and
rules; an unmapped construct breaks generation.

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
