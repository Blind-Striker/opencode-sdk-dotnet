# M2 Second Breadth Batch Execution Plan

Date: 2026-08-15

**Goal:** make `v2.session.remove`, `v2.session.rename`, and the `v2.shell.*` family
(`list`, `create`, `get`, `remove`, `timeout`, `output`) callable through the sealed
Q93/Q94 marshalling — the design-prover batch: every new mechanism the M3 Arc 2 seals
introduced gets exercised by a real operation. Sealed inputs: research log Session 25
(Q93 placement map, Q94 dual-channel location), #28 (rides this batch), ADR-0008/0009.

## What the pinned spec says (recon, 2026-08-15)

- `shell.list` GET `location`; `shell.create` POST body (`command`+`timeout` required,
  `cwd`, free-form `metadata` object) + `location`; `shell.get`/`shell.remove` path `id`
  + `location`; `shell.timeout` PATCH body + `location`; `shell.output` GET `id` +
  `location` + integer `cursor`/`limit` — its response data is a flat
  `{output, cursor, running, …}` object, **not** the `ListCursor` shape, so it binds as
  a plain operation (no `ListRequest` derivation; the profile wall keeps it flat).
- **New envelope profile:** every shell success wraps as `{location: Location.Info,
  data: …}` (both required) — the location-echo envelope. The sibling surfaces on the
  response like the cursor precedent (`response.Location`).
- **204 No Content** on `session.remove`, `session.rename`, `shell.remove` — the first
  bodyless success shape. Fail-closed rendering: a 204 with a non-empty body is a
  protocol failure (RFC 9110: 204 carries no content), and the response envelope has no
  payload property.
- `Shell.Info1` is byte-identical to `Shell.Info` including every constraint — the
  second `schemaAliases` row (Q81 mechanism).
- `shell.create.metadata` is `{"type":"object"}` free-form — treatment decided at the
  binder wall when admission reaches it (existing dialect rules first; a new mechanism
  only if the walls refuse).

## Machinery this batch lands

1. Binder **placement map** (Q93): per-property wire placement on the operation plan;
   the body+query double-derivation refusal retires exactly for admitted ops.
2. **Location rendering** (Q94): generated `Location` property on location-carrying
   request records; deepObject marshalling (`location[directory]=…`) once in route
   composition; `OpenCodeClientOptions` ambient default riding the
   `x-opencode-directory`/`x-opencode-workspace` headers in pipeline decoration.
3. The `{location, data}` **envelope profile** and the **204 bodyless** envelope.
4. **PATCH and DELETE** verbs through routes/emitters/pipeline.
5. The **`Shells` client family** — the roster contract test must force its DI
   registration.
6. `message.list` query values remain faithful to their pinned schemas. The former #28
   description-derived `order`+`cursor` refusal was removed by #54 under ADR-0013.

## Boundaries

- Standing walls and stop conditions carry; spec pin stays `a6a712a`.
- Curation rows are API surface: names follow the mechanical rules
  (`ShellsClient`, `ShellCreateRequest`, …); anything the rules cannot produce stops for
  the maintainer.
- Performance guardrails: no hot-path work; baselines must not regress.

## Work plan

- [x] Placement map in the binder, red-test-first: a body+query mix merges into one
      uniform request model exactly when every query property is the location selector
      (`QueryRequestPlan.RidesRequestBody`); the model gains `[JsonIgnore]` query-side
      properties, the route builder types itself with the body model, and every other
      mix keeps the deliberate wall.
- [x] Location: the hand-written `LocationSelector` spine (the wire's query selector is
      an inline `{directory?, workspace?}` object, not `Location.Info`, so it follows the
      `SessionParentFilter` precedent) with request-record property + deepObject route
      composition; ambient header default in options/pipeline (explicit query wins by
      server precedence — no client merge logic).
- [x] Envelope profiles: `{location, data}` (object and list variants) and 204-bodyless,
      with binder walls and emitter snapshots; contract tests land with admission.
- [x] Curation rows + `Shell.Info1` alias; regen; model closure; PATCH/DELETE (PATCH
      rides the internal `OpenCodeHttpMethod` spine — `HttpMethod.Patch` is absent
      downlevel); operation methods + contract tests per op. The verb vocabulary gained
      `remove`/`rename`/`timeout`, `RequestTypeName` folds Get like the response name,
      and reachability recollects post-alias so an alias target's promoted inline
      children stay bound; the alias comparer resolves promoted references structurally.
      **`v2.shell.output` deferred:** its inline data object and integer cursor/limit
      query params each need a mechanism no other admitted operation needs; it returns
      with a later batch.
- [x] `message.list` query composition is generated from the pinned parameter schemas. #54
      removed the former `mutuallyExclusiveQueries` section and no-send guard because their
      premise existed only in description and implementation source.
- [x] Live demo against `opencode2 serve` v0.0.0-next-17403 (2026-08-16): shell
      create → list → get → timeout → remove (204), session rename → remove (204), all
      typed; the ambient options location rode the header channel and came back in the
      location echo. (`output` deferred with its operation.)
- [x] ROADMAP + this plan updated in the same commits.
