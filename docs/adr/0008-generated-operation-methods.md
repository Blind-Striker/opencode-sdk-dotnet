# All operation methods are generated; excluded operations are fingerprint-pinned

Date: 2026-08-17

Every operation method on the generated surface is generator-emitted
as a one-line delegation into the hand-written behavior core; behavior (retry, error
mapping, `NoThrow`, telemetry) never lives in method bodies. Hand-written op methods
sit outside CI regen-verify and go silently stale as upstream moves — generated methods
turn every spec drift into a loud diff or a broken build (research doc 06 §1's earlier
hand-written-surface position was overturned by the maintainer on this argument).
Hand-written remains the identity core: transport pipeline, SSE engine, launcher,
exception hierarchy, envelope base, options types, DI extensions.

**Streaming operations are generated on the same rule.** An SSE endpoint is an ordinary
HTTP response the client reads incrementally, and the pinned contract declares it in
full — `text/event-stream` content, the `{id, event, data}` frame as the *decoded* envelope
(the wire omits `event:` and `id:` for an ordinary payload), and `data`'s payload through
`contentSchema`/`contentMediaType`. So the drift argument applies unchanged: the
stream *engine* is behavior and stays in the core, while stream *endpoints* emit as
one-line delegations into it, exactly like the one-shot surface. A stream yields
`IAsyncEnumerable<T>` rather than a response envelope, which is why its signature carries
no per-call options (ADR-0007). Upstream's own generator emits its SSE operations the same
way, over the same contract.

The radar covers both sides of the boundary: generated output is CI regen-verified
(ADR-0003); every **excluded** operation (`pty.connect`, future exclusions) is
**fingerprint-pinned** — the generator hashes the operation's full subtree (method, path,
parameters, content types, transitive schemas) into a committed manifest, and a spec
refresh that moves a pinned construct breaks the build for explicit review. Exclusion is
reserved for transports the HTTP pipeline cannot carry: `pty.connect` upgrades to
WebSocket, which leaves HTTP after the handshake and needs a different client stack
entirely. Non-JSON response bodies stay generated via a fail-closed
content-type→payload map (`application/octet-stream` → `Stream` payload on a disposable
envelope; `text/*` → `string`); an unknown content type breaks generation.

## Consequences

- The generator emits public API, so its emission rules (naming map, envelope payload
  names, guard clauses, XML docs) are review surface — curation config changes are API
  reviews.
- Bound handles are opt-in group curation: a row declares a client name, handle name,
  and required path parameter. The Binder applies one mechanical rule to that declaration:
  operations carrying the parameter emit on the handle with it partially applied;
  collection operations stay on the collection client. Groups without a handle declaration
  stay flat. Emitters never branch on operation IDs, wire group names, or concrete client
  names.
- An operation name the mechanical policy cannot derive without semantic guessing rides a
  reason-bearing, fail-closed curation row. The binder validates the final identifier and all
  collisions; emitters consume the bound name mechanically and never branch on an operation ID.
  The first concrete case is `v2.event.subscribe`, whose reviewed surface is
  `Events.SubscribeAsync`, not the natural `Events.GetSubscribeAsync` derivation.
- This curation changes only .NET naming. It cannot repair a type, constraint, format, or
  validation missing from the pinned OpenAPI document (ADR-0013).

Evidence: research log Q102.
