# Roadmap

Date: 2026-08-15

Operational state: what is done, what is next, what is open. This file shrinks as work lands.
Evergreen rules and locked decisions live in `../AGENTS.md`; decision records in `adr/`.

## Status

**M1 is complete.** The SDK targets the v2 protocol surface only (ADR-0005; `spec/` pins
v2-branch commit `a6a712a`), and the walking skeleton runs end to end: SpecIR plus curation
bind into client/operation/envelope/route/error plans, generic emitters render the callable
surface as committed source under `src/OpenCode.Sdk` (`OpenCodeClient.GetHealthAsync`,
`Sessions.GetSessionClient(...)`, `SessionClient.GetMessageAsync`, guarded envelopes,
`OpenCodeRoutes`, response adapters), and one hand-written `Pipeline` owns endpoint authority,
Basic-auth/User-Agent decoration, buffering, throw-versus-`NoThrow`, and transport-failure
mapping. The public API is locked in a reviewed `PublicApiGenerator` baseline; the writer
refuses unmanifested overwrites and headerless manifest entries; packing still fails on the
partial-operation marker while breadth is pending. Demonstrated live 2026-08-13 against
`opencode2 serve` v0.0.0-next-17403 (pin `a6a712a` — deliberate version skew accepted): both
generated methods returned typed 200 payloads (`ServiceHealth`; `SessionMessageAssistant`
with its wire `id`).

A verified multi-agent review of PR #16 (2026-08-13) produced a milestone-anchored queue:
the blocker set (typed-spine leaks, silent route rewriting) lands on the PR branch itself
(#17) together with the performance-test infrastructure (#18); every other finding lives in
issues #19–#25 pinned to the milestone that resolves it — nothing on that list outlives the
M series.

**M2's first breadth batch is complete** (plan:
`superpowers/plans/2026-08-14-m2-first-breadth-batch.md`; decisions: research log Sessions
19–20). `session.list`, `session.get`, `session.create`, and `message.list` are callable
through their final generated surface — uniform `*Request` operation inputs (Q83) with the
query records riding the `ListRequest` seam, `SessionCreateRequest` bodies through the
pipeline's JSON path, cursor-list envelopes with the shared `ListCursor`, query-composing
routes, the `Session.Info` model closure, and the first 5xx arm. Riders #19 (carrier converters), #21 (fail-closed walls + P2 single-pass
envelopes: `GetMessageAsync` 67.4→56.2 μs, `ListMessagesAsync` baseline 58.1 μs/28.24 KB),
and #22 (List/Create verb rules + C17–C20) landed with it; upstream's `InvalidRequestError1`
duplicate collapses through the new `schemaAliases` curation. Demonstrated live 2026-08-14
against `opencode2 serve` v0.0.0-next-17403 (create → list → get → messages, wire cursor
round-tripped). The #20 decision landed (blank explicit passwords refuse; the environment
fallback was later removed by Q90 — `null` sends anonymous requests) and #25 closed keep. The alignment batch is complete — the uniform
`*Request` rename (Q83), the feature-slice layout migration (Q84), and the Extensions
bring-up (Q85) — and the follow-on construction/options/DI reshape (research log Q90)
landed on the same PR: options-only construction with the read-only
`IOpenCodeClientOptions` view and configurable `Username`, no SDK environment reads,
`IHttpClientFactory`-based `AddOpenCode` returning the `IHttpClientBuilder`, pooled
connection lifetime on the owned transport, and the sandbox as the Generic Host DI
showcase. The PR #26 external review ran through adversarial verification (36 findings:
30 confirmed / 4 plausible / 2 refuted): the ten merge blockers plus small fold-ins are
queued as the fix batch in `agents/handover-prompts/HANDOFF-2026-08-14-3.md` — Q91 sealed
2026-08-15 (research log Session 23, doc 16), unblocking blocker #1 — and every surviving
non-blocker lives in issues #27–#30 and the #24 hygiene comment. Merge follows the fix
batch; further breadth batches follow merge.

## Milestones

Deliverable-first: every milestone ends in something callable or demonstrable. The next
milestone gets a short (1–2 page) plan when it starts — never earlier. Ordering beyond M2
is revisited at each milestone boundary.

1. **M1 — Walking skeleton.** `v2.health.get` + `v2.session.message` end to end
   (SpecDocument → Binder → EmitPlan → Roslyn emitters → committed source under
   `src/OpenCode.Sdk` → minimal transport core → callable client), demonstrated once by
   hand against a real `opencode2 serve` with the output pasted into the PR. Arc B opens
   with the v2 retarget task (pin snapshot, ingestion-wall admit rule, regenerated
   closure). Two independently mergeable arcs: selected compiler + committed models
   (landed), then the callable client with typed errors and `NoThrow`. Design reference:
   `superpowers/specs/2026-08-11-production-walking-skeleton-design.md`.
2. **M2 — Breadth batches.** The generation profile grows in vertical operation batches;
   each batch lands its curation rows, reachable models, operation methods, and contract
   tests together. The first batch (list/get/create/message-list) is complete with every
   review rider resolved (#19–#22, #20, #25), and so is the alignment batch (uniform
   `*Request`, feature-slice layout, Extensions bring-up — research log Q83–Q85). The
   Extensions package grows in parallel with the remaining batches.
3. **M3 — Streams.** Planning opens with the **location + merged-Request input design
   session** (sealed 2026-08-14, research log Session 22): the dual-channel location
   mechanism, deepObject marshalling, the one-`*Request`-carrying-body-and-query shape,
   and the `session.list` flat-field exception — census and mechanisms in research doc 15
   §5a/§6. Then the SSE engine over the v2 stream surface (`v2.event.subscribe`,
   `v2.session.log` with `after`/`follow`, cursor-paged `v2.message.list`); the v1
   durable-stream design does not carry over and is re-derived here. Demo: watching a
   real session's event stream. The net472 `ServicePointManager` item lands here. The
   union single-pass deserialization and streaming adapter-boundary redesign (#23) land
   on the M3 runway, gated on the performance baselines (#18).
4. **M4 — Launcher.** `OpenCodeServer.StartAsync` with three-OS acceptance (ADR-0001)
   over `opencode2 serve`; demo: the SDK starts the server itself and calls health. The
   net472 stdout/tree-kill items land here. (`serve --stdio`'s stdin leash and the
   background service's discovery file are candidate mechanisms — decided in the M4 plan;
   platform detail: research doc 15.)
5. **M5 — Full surface.** Complete generation profile over the protocol surface,
   exclusion fingerprints (ADR-0008), packaging unblocked.
6. **M6 — Operational closure.** `refresh-spec`, retry/telemetry/hooks, quarantine
   lane, nightly canary (the performance suite joins it); durable decisions distill
   into ADRs and the `superpowers/` documents retire. Any
   hygiene-sweep leftovers (#24) are resolved here — nothing from the review queue
   survives the M series.

## Open Questions

- **v2 GA watch** — the v2 line ships as `opencode2` (npm `@opencode-ai/cli@next`, desktop
  beta via `update.opencode.ai`) with no GA date; the spec pin stays a deliberate snapshot,
  refreshed at milestone boundaries. Platform detail: research doc 15.
- **`v2.session.log` semantics** — it replaces the v1 durable stream (`after` + `follow` on
  an experimental path); resume guarantees are unestablished. Owned by M3.
- **Spec refresh cadence** — the `refresh-spec` tool lands in M6; the cadence policy stays
  open.
- **Structural-union emission shape** — the v1 pin had five structural-union sites
  (`Config.formatter` et al.); the population is re-censused at the retarget, and the
  emission decision lands when a breadth batch first reaches one (a public API review).
- **Release mechanics** — decided parts live in ADR-0006 (independent semver, per-merge
  GitHub Packages CD, manual NuGet.org releases). Pre-1.0 numbering, `VersionPrefix`,
  RELEASE_NOTES flow, and the concrete workflows are scheduled when the first publishable
  increment approaches.

## Known Gaps

- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
