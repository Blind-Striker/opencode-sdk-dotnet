# Roadmap

Date: 2026-08-10

Operational state: what is done, what is next, what is open. This file shrinks as work lands.
Evergreen rules and locked decisions live in `../AGENTS.md`; decision records in `adr/`.

## Status

Structural skeleton in place: `OpenCode.slnx`; `src/OpenCode.Sdk` + `src/OpenCode.Sdk.Extensions`
(empty multi-targeted shells, full TFM matrix); `tests/OpenCode.Sdk.Tests` (TUnit smoke test,
net472 leg Windows-only); three-OS CI (`.github/workflows/ci.yml`: build + test + TRX reporting);
pinned OpenAPI snapshot (`spec/openapi.json`, provenance in `spec/SNAPSHOT.md`). Packages current
as of 2026-08-08. Design runway complete and grill-hardened: three sealed specs under
`superpowers/specs/` (public API, generator architecture, testing architecture), ADRs 0001–0009,
research log sessions 1–9. Implementation planning complete (2026-08-10): slice map at
`superpowers/plans/2026-08-10-implementation-slice-map.md` (12 vertical slices), slice issues
**#1–#12** with native `blocked_by` ordering; deviation protocol added
(`docs/agents/deviation-protocol.md`), falsifiability working agreement added to `AGENTS.md`.
The Slice 0 tooling skeleton has landed. Repo-local Slopwatch pinning, a committed zero-entry
baseline, and the Linux CI gate are active. Slice 1 planning is complete: the parser + SpecIR
plan (`superpowers/plans/2026-08-10-slice-01-parser-specir.md`) is sealed, issue #2 is
`ready-for-agent`, and generator spec §4.1 gained four pinned-spec dialect corrections
(research log session 11); next is the Slice 1 execution session
(`docs/agents/handover-prompts/HANDOFF-2026-08-10.md`).

## Queue

In order — do not improvise beyond it without asking the maintainer.

1. **SDK build-out — remaining slice issues #2–#12.** Scope, sequencing, and the per-slice done
   definition live in the slice map
   (`superpowers/plans/2026-08-10-implementation-slice-map.md`); task-level progress lives in
   per-slice plan checkboxes (plans written just-in-time; a slice whose plan exists gets
   `ready-for-agent`). Execution: `subagent-driven-development` per slice on a
   `feature/slice-NN-*` worktree branch; one PR to master closes the issue; per-task commits on
   the slice branch are the agreed development loop (the `AGENTS.md` commit-rule exception).
   TDD for transport/SSE/launcher; `api-design` (extend-only) + `snapshot-testing` (Verify)
   lock the public surface as implementation lands (testing spec §12; slices 4–5). **Spec
   retirement rides slice 11:** durable sealed decisions without an ADR home distill into ADRs
   before the transient `docs/superpowers/` documents are deleted.
2. **Later** — **MCP server**: in this repo as a thin SDK adapter (ADR-0006). Tech targeting
   decided 2026-08-08 (research doc 05), **re-sealed at phase start against the then-current
   MCP landscape:** the 2026-07-28 protocol revision via MCP C# SDK v2.0, stdio + streamable
   HTTP, no investment in deprecated features. Evaluate NuGet's `McpServer` package type for
   distribution; its SDK usage defines the deep-tested legacy set (ADR-0005). Aspire AppHost
   for local dev/test (mini UI, `opencode serve` as a resource) — planned. "opencode HQ" —
   multi-instance aggregation above the SDK — is a valued future deliverable in its own
   right, not SDK scope.

## Open Questions

- **net472 spike items** — distributed to their SUT slices (slice map, sealed decision 4):
  polyfill-set validation + generated-model downlevel compile → slice 3 (the 5-TFM
  milestone; checklist in generator spec §12); async stdout reading + `taskkill /T /F`
  tree-kill → slice 6; SSE long-lived-response behavior
  (`ServicePointManager.DefaultConnectionLimit = 2`) → slice 8.
- **Spec tracking:** `openapi.json` changes on every upstream push — snapshot per SDK release +
  diff/regen workflow (`spec/SNAPSHOT.md` is the pin; the submodule tracks upstream). The
  `refresh-spec` tool lands in slice 11; the refresh **cadence** policy stays open.
- **Launcher deep-dive items** (public API spec §13; doc 06 §3) — owned by slice 6, including
  release-binary confirmation of `--port 0`.
- **Release mechanics** — decided parts live in ADR-0006 (independent semver, per-merge GitHub
  Packages CD, manual NuGet.org release pipeline). Still open: pre-1.0/preview numbering,
  `VersionPrefix`, RELEASE_NOTES flow, the concrete workflows. **Not covered by the slice
  map** — scheduled when the first publishable increment approaches.

## Known Gaps

- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
