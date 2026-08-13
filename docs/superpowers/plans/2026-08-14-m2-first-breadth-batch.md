# M2 First Breadth Batch Execution Plan

Date: 2026-08-14

**Goal:** make `v2.session.list`, `v2.session.get`, `v2.session.create`, and
`v2.message.list` callable through their final generated SDK surface. This batch
deliberately opens the three machines M1 walled off: query-parameter binding, request
bodies (the first non-GET verb), and list envelopes with cursor pagination.

**Architecture:** unchanged from Arc B — SpecIR plus curation bind into plans, generic
name-blind emitters render them, generated methods delegate once into the hand-written
`Pipeline`. The `Pipeline` grows exactly one capability: sending a JSON request body
serialized through the source-generated context.

## What the pinned spec says (recon, 2026-08-14)

- `session.list` — GET, **9 optional query parameters**, all `anyOf [T, null]`:
  `limit` (string-typed in the spec), `order` (`asc`/`desc` enum), `search`, `cursor`
  (opaque), `parentID` (with a literal `"null"` string variant meaning root-only), and the
  location-scoping family `workspace`/`directory`/`project`/`subpath` (flat params — the
  deferred `location[...]` question resolves to these). 400 is a **multi-tag anyOf**
  (`InvalidCursorError` | `InvalidRequestError1` | `InvalidRequestError`).
- `session.create` — POST, inline request-body object (all-optional nullable properties:
  `id`, `title`, `agent`, …), responds `{data: Session.Info}`.
- `session.get` — GET path param, `{data: Session.Info}`, 404 `SessionNotFoundError`.
- `message.list` — GET path + `limit`/`order`/`cursor`, responds the same envelope shape,
  and declares **500 `UnknownError`** — the first 5xx in a status map.
- Both list responses share one envelope: `{data: [...], cursor: {previous?, next?}}` —
  one mechanical list-envelope shape covers both.
- New reachable closure: the `Session.Info` graph (13 properties, nested
  time/tokens/location/revert) plus `InvalidCursorError`, `UnknownError`, and the
  dotted-dup name `InvalidRequestError1` (naming resolution must stay mechanical or fail
  closed into a curated override).

## Boundaries

- Arc B boundaries carry over verbatim: opencode-dialect-only, fail-closed on any
  unsupported selected shape, name-blind emitters, curation-driven public API, mechanical
  payload names with collision-only overrides, optionality/nullability independence,
  non-2xx stays on the API-error channel (now including 500), undeclared 2xx is a
  protocol failure.
- The Arc B stop-condition list carries unchanged: binder needing raw OpenAPI,
  name-branching emitters, structural unions, ownership ambiguity, TFM breakage, partial
  packs.
- **Spec pin stays at `a6a712a`** for this batch — the pin is one day old; refreshing
  buys nothing but churn. Next sanctioned refresh moment: M3 boundary.
- Cursor **pagination surface stays raw** in this batch: response envelopes expose the
  wire cursor (`Previous`/`Next`), request options accept an opaque `cursor` string. A
  paginator/`IAsyncEnumerable` convenience is an M3 candidate, designed together with the
  stream surface — not here.

## Public-API decisions (maintainer, sealed 2026-08-14)

1. **Query surface:** generated per-operation options records deriving from one shared
   `ListOptions` base that carries only the cursor-pagination trio (`Limit`, `Order`,
   `Cursor`). The generator owns a fail-closed **profile-detection wall**: an operation
   derives from the base only when its wire parameters match the profile exactly;
   otherwise it gets flat standalone options. The base is the typed seam the M3 paginator
   consumes. Dichotomy: `*Options` shapes the call (query), `*Request` is the wire body.
2. **`limit` is `int?`** — invariant conversion to the wire string at the route boundary;
   non-positive values refused with `ArgumentException`. `uint` rejected: FDG bans
   unsigned in public APIs (CLS), zero ecosystem precedent, and it does not buy the
   invariant (0 stays representable).
3. **`parentID` rides `SessionParentFilter`** — a small hand-written public spine type
   (`RootOnly` singleton, `Of(id)` factory); the binder recognizes the wire shape
   mechanically (`anyOf` of a patterned string and the literal `"null"` enum), never the
   parameter name. Invalid states are unrepresentable; no magic strings.
4. **Create body model is `SessionCreateRequest`** with an optional parameter —
   `CreateSessionAsync()` sends an empty body (every property is optional on the wire).
5. **`List*`/`Create*` verb rules** join `OperationNamePolicy`, including the C18
   structural-verb-position fix (#22).

## Tasks

- [x] **0. Decision batch** — the five decisions above, sealed with the maintainer
  2026-08-14 (research log Session 19).
- [ ] **1. Curation + binding** — curation rows for the four operations; binder support
  for optional nullable query parameters (fail-closed on any other query shape) and for
  inline JSON request bodies bound into a generated request model; multi-tag 400 and the
  500 arm enter the status-map machinery.
- [ ] **2. Model closure** — regenerate with the `Session.Info` graph, the new error
  types, and the list envelopes; resolve `InvalidRequestError1` naming mechanically or
  fail closed into a curated override; API baseline review.
- [ ] **3. Pipeline body path** — `ExecuteAsync` overload (or body-carrying request
  shape) serializing through `OpenCodeJsonContext`; `Content-Type: application/json`;
  covered by pipeline tests (body bytes, header, GET stays body-less).
- [ ] **4. Emitters** — operation methods carrying options records and body models; route
  builders composing escaped query strings; list-envelope response adapters; the
  generator fail-closed walls batch (#21) lands here, where breadth first stresses those
  walls, with P2's single-pass `{data}` DTO folded into the envelope design.
- [ ] **5. Naming batch (#22)** — `List*`/`Create*` rules + C18 fix; verify C17/C19/C20
  against the wall and fix what holds.
- [ ] **6. Hardening riders** — F07 concrete `Unknown*` converter (#19); decisions #20
  (password semantics) and #25 (IVT) to the maintainer alongside task 0.
- [ ] **7. Contract matrix + perf** — contract tests over the four operations (success,
  every declared error status, cursor round-trip, undeclared-2xx, empty-marker); extend
  `ClientOperationBenchmarks` with a `ListMessagesAsync` benchmark once the surface
  exists.
- [ ] **8. Close-out** — live sandbox demo (create → list → get → messages) against
  `opencode2 serve`; ROADMAP status update; research-log entries for decisions sealed
  during execution.
