# Roadmap

Date: 2026-08-13

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

The next work is M2 — the first breadth batch, planned when it starts.

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
   tests together. Opens with the review hardening batch (#19 `Unknown*` serialization,
   #20 password-semantics decision, #25 IVT decision); the generator fail-closed walls
   batch (#21, includes the P2 envelope fold) and the naming/curation wall batch (#22)
   land at this boundary.
3. **M3 — Streams.** SSE engine over the v2 stream surface (`v2.event.subscribe`,
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
6. **M6 — Operational closure.** `refresh-spec`, Extensions DI breadth,
   retry/telemetry/hooks, quarantine lane, nightly canary (the performance suite joins
   it); durable decisions distill into ADRs and the `superpowers/` documents retire. Any
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
