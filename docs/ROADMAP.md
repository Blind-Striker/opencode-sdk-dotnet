# Roadmap

Date: 2026-08-13

Operational state: what is done, what is next, what is open. This file shrinks as work lands.
Evergreen rules and locked decisions live in `../AGENTS.md`; decision records in `adr/`.

## Status

Structural skeleton, three-OS CI, the pinned spec snapshot, Slice 0 tooling, and complete-pin
SpecIR ingestion are in place. M1's compiler arc selects the two walking-skeleton operations,
binds their reachable closure, emits committed Roslyn-built models plus source-generated JSON
metadata, and owns deterministic `generate` / `generate --verify` writes through a guarded
manifest. The partial-operation marker blocks every package while breadth remains incomplete.
The active work is M1 Arc B's minimal transport and callable client with typed errors and
`NoThrow`; see the just-in-time
[M1 Arc B callable-client plan](superpowers/plans/2026-08-13-m1-arc-b-callable-client.md).

## Milestones

Deliverable-first: every milestone ends in something callable or demonstrable. The next
milestone gets a short (1–2 page) plan when it starts — never earlier. Ordering beyond M2
is revisited at each milestone boundary.

1. **M1 — Walking skeleton.** `v2.health.get` + `v2.session.message` end to end
   (SpecDocument → Binder → EmitPlan → Roslyn emitters → committed source under
   `src/OpenCode.Sdk` → minimal transport core → callable client), demonstrated once by
   hand against a real `opencode serve` with the output pasted into the PR. Two
   independently mergeable arcs: selected compiler + committed models, then the callable
   client with typed errors and `NoThrow`. Design reference:
   `superpowers/specs/2026-08-11-production-walking-skeleton-design.md`.
2. **M2 — Breadth batches.** The generation profile grows in vertical operation batches;
   each batch lands its curation rows, reachable models, operation methods, and contract
   tests together.
3. **M3 — Streams.** SSE engine, live/durable subscribe with `after` resume; demo: watching
   a real session's event stream. The net472 `ServicePointManager` item lands here.
4. **M4 — Launcher.** `OpenCodeServer.StartAsync` with three-OS acceptance (ADR-0001);
   demo: the SDK starts the server itself and calls health. The net472 stdout/tree-kill
   items land here.
5. **M5 — Full surface.** Legacy hub, complete generation profile, exclusion fingerprints
   (ADR-0008), packaging unblocked.
6. **M6 — Operational closure.** `refresh-spec`, Extensions DI breadth,
   retry/telemetry/hooks, quarantine lane, nightly canary; durable decisions distill into
   ADRs and the `superpowers/` documents retire.

## Open Questions

- **Spec refresh cadence** — the `refresh-spec` tool lands in M6; the cadence policy stays
  open.
- **Structural-union emission shape** — the five structural-union pin sites
  (`Config.formatter` et al.) need an emission decision when a breadth batch first reaches
  one (a public API review).
- **Release mechanics** — decided parts live in ADR-0006 (independent semver, per-merge
  GitHub Packages CD, manual NuGet.org releases). Pre-1.0 numbering, `VersionPrefix`,
  RELEASE_NOTES flow, and the concrete workflows are scheduled when the first publishable
  increment approaches.
- **Upstream v2 watch:** the active upstream `v2` branch publishes its spec at
  `packages/protocol/openapi.json`; its dialect adds single-element `allOf` wrappers
  (validation keywords, occasionally with annotations), keeps `v2.`-prefixed operationIds,
  and returns literals to single-value `enum` (research log sessions 12–13). No plan impact
  today; re-check at each spec refresh and before the upstream-absorbing major.

## Known Gaps

- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
