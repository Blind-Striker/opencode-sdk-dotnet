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
envelopes; the standing baselines live in the M3 plan and are measured against
wire-shaped payloads),
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
30 confirmed / 4 plausible / 2 refuted), and the verified fix batch landed 2026-08-15
red-test-first — all ten merge blockers plus the small confirmed fold-ins, with Q91 sealed
(research log Session 23, doc 16: the caller-owned HttpClient constructor stays public
behind a fail-closed anonymous-mode guard). A second full-diff review (15 verified
findings) closed the same day: six runtime/emitter fixes landed on the branch (timeouts
route onto the transport spine, conflicting BaseAddress refuses, handle guards match the
route guards, carrier payload guards, count-guard identity, the Pagination shadow wall)
for 932 green tests, and every surviving finding lives milestone-anchored with an explicit
trigger — #31 (M2, DI lifetime shape, decision-first), #32 (M3, EscapeDataString TFM
policy), #33 (M3, carrier hand-construction semantics, rides #23), plus the #27 and #23
enrichments. PR #26 is merged. The #34 contract-test consolidation landed as its own
follow-up PR: one shared contract scaffold (`ContractScenario`, `WireBodyData`), a single
`FixtureLoader`/`RecordingHttpHandler` pair compiled from `tests/Shared` into both the SDK
and Extensions test projects, and the previously unobserved object-envelope
`{"data":null}` refusal pinned by test (the support pair under `tests/Shared` is consumed
by the SDK test project; the Extensions tests are registration-topology only).

**M3 is open** (plan: `superpowers/plans/2026-08-15-m3-plan.md`; decisions: research log
Session 24). Q92's simplicity-first construction (ADR-0010) is landed — Arc 1 complete:
the transport constructor is internal friend-assembly test surface, Extensions registers
a factory-less singleton client family with a roster contract test, and the Q91 guard
machinery is deleted — closing #31 by construction. #32 and #33 stay sealed with their
execution homes recorded (Session 24), and the location + merged-Request design is
sealed (Session 25, Q93/Q94: the binder placement map and the dual-channel location
rendering).

**The M2 second breadth batch is complete** (plan:
`superpowers/plans/2026-08-15-m2-second-breadth-batch.md`) — the design-prover batch:
`session.remove`/`session.rename` and the `Shells` family
(`list`/`create`/`get`/`remove`/`timeout`) ride the 204 no-content and
`{location, data}` envelope machinery, the deepObject `LocationSelector` query channel,
the merged body+query request models, the ambient options location riding the
middleware headers, and the first PATCH/DELETE verbs. #28 landed as the
`mutuallyExclusiveQueries` curation section with the route-boundary refusal on
`message.list`. Demonstrated live 2026-08-16 against `opencode2 serve`
v0.0.0-next-17403 (shell create → get → timeout → remove and session rename → remove,
204s typed, ambient location echoed). `v2.shell.output` deferred to a later batch —
its inline data object and integer cursor query params each need a mechanism of their
own. 13 operations selected, 107 pending.

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
3. **M3 — Streams.** Plan: `superpowers/plans/2026-08-15-m3-plan.md`. The Q92
   construction reshape (ADR-0010) opens the runway as its own PR. The **location +
   merged-Request design session** (sealed proactive 2026-08-14, research log Session 22;
   census in research doc 15 §5a/§6) seals the marshalling surface. Then the SSE engine
   over the v2 stream surface (`v2.event.subscribe`, `v2.session.log` with
   `after`/`follow`, cursor-paged `v2.message.list`); the v1 durable-stream design does
   not carry over and is re-derived here. Demo: watching a real session's event stream.
   The net472 `ServicePointManager` connection-lease item lands here as a GA gate. The
   union single-pass deserialization and streaming adapter-boundary redesign (#23) land
   on the M3 runway, gated on the performance baselines (#18), together with the
   second-review perf mechanisms and #29; #32 (uniform route-boundary refusal) rides the
   net472 cluster and #33 (carrier construction refusal) rides #23.
4. **M4 — Launcher.** `OpenCodeServer.StartAsync` with three-OS acceptance (ADR-0001)
   over `opencode2 serve`; demo: the SDK starts the server itself and calls health. The
   net472 stdout/tree-kill items land here. (`serve --stdio`'s stdin leash and the
   background service's discovery file are candidate mechanisms — decided in the M4 plan;
   platform detail: research doc 15.)
5. **M5 — Full surface.** Complete generation profile over the protocol surface,
   exclusion fingerprints (ADR-0008), packaging unblocked. The **ambient location
   header decision (#37)** lands here and is decision-first: packaging unblocking
   freezes the public surface, so this is the last free moment to drop
   `OpenCodeClientOptions.Location` (option B) or fold it into the query channel
   (option C). The batch admitting `project.list` / `permission.saved.*` /
   `session.form.*` answers whether the header is those operations' only addressing
   channel.
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
