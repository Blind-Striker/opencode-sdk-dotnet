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
The per-Session event stream; replayable and resumable via the `after` cursor.
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

**Instance**:
One running opencode server process; the API is bound to a single Instance.

**Directory targeting**:
Per-request project targeting via the `x-opencode-directory` header.

**PTY**:
A pseudo-terminal session managed through the API.

### This project's language

**Modern surface**:
The `v2.*`-prefixed operation block of the pinned spec; public names carry no prefix.
_Avoid_: v2, V2 (in public naming)

**Legacy surface**:
The un-prefixed operation block of the pinned spec; lives behind a legacy-marked sub-surface
and is deleted wholesale at the 2.0-absorbing major.

**Launcher**:
The in-core component that starts, monitors, and stops a local `opencode serve` process.

**Spec pin**:
The committed copy of upstream's `openapi.json` under `spec/`, provenance in `SNAPSHOT.md`.

**Model layer**:
The generated types and serializer registry shipped inside `OpenCode.Sdk`.
