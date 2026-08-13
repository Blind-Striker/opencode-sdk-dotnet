# All operation methods are generated; hand-wired constructs are fingerprint-pinned

Date: 2026-08-13

Every operation method on the generated surface is generator-emitted
as a one-line delegation into the hand-written behavior core; behavior (retry, error
mapping, `NoThrow`, telemetry) never lives in method bodies. Hand-written op methods
sit outside CI regen-verify and go silently stale as upstream moves — generated methods
turn every spec drift into a loud diff or a broken build (research doc 06 §1's earlier
hand-written-surface position was overturned by the maintainer on this argument).
Hand-written remains the identity core: transport pipeline, SSE engine and
stream-endpoint wiring, launcher, exception hierarchy, envelope base, options types,
DI extensions.

The radar covers both sides of the boundary: generated output is CI regen-verified
(ADR-0003); every **excluded or hand-wired** operation (SSE endpoints, `pty.connect`,
future exclusions) is **fingerprint-pinned** — the generator hashes each such operation
into a committed manifest (excluded ops: the full subtree — method, path, parameters,
content types, transitive schemas; hand-wired ops: the transport shape only, their item
schemas being generated and regen-verified already), and a spec refresh that moves a
pinned construct breaks the build for explicit review. Non-JSON response bodies stay generated via a fail-closed
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
