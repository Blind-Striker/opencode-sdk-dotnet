# Roadmap

Date: 2026-08-10

Operational state: what is done, what is next, what is open. This file shrinks as work lands.
Evergreen rules and locked decisions live in `../AGENTS.md`; decision records in `adr/`.

## Status

Structural skeleton in place: `OpenCode.slnx`; `src/OpenCode.Sdk` + `src/OpenCode.Sdk.Extensions`
(empty multi-targeted shells, full TFM matrix); `tests/OpenCode.Sdk.Tests` (TUnit smoke test,
net472 leg Windows-only); three-OS CI (`.github/workflows/ci.yml`: build + test + TRX reporting);
pinned OpenAPI snapshot (`spec/openapi.json`, provenance in `spec/SNAPSHOT.md`). Packages current
as of 2026-08-08. Codegen spike complete (research doc 08). Grill session complete: ADRs
0001–0006 backfilled, root `CONTEXT.md` glossary created, ADR rules canonicalized in
`adr/README.md`. Public API design complete and grill-hardened — spec at
`superpowers/specs/2026-08-09-public-api-design.md`; the grill session (2026-08-09) produced
ADRs 0007–0009, research docs 11–12, and corrections throughout the spec. Generator
architecture designed and grill-hardened — spec at
`superpowers/specs/2026-08-09-generator-architecture.md`; its grill (2026-08-09, research log
session 7) run-proved the converter dispatch shape and hardened the spec in place. Testing
architecture & strategy designed — spec at
`superpowers/specs/2026-08-10-testing-architecture-design.md` (upstream test-infra
verification and reference-repo CI patterns: research log session 8). No SDK code yet —
next up: the holistic grill session (all three specs, testing-strategy focus), then
`writing-plans`.

## Queue

In order — do not improvise beyond it without asking the maintainer. Parenthetical skill notes
are hints for the driving agent.

1. **Design runway — remaining steps.** All three design specs are done and sealed
   (`superpowers/specs/2026-08-09-public-api-design.md`,
   `superpowers/specs/2026-08-09-generator-architecture.md`,
   `superpowers/specs/2026-08-10-testing-architecture-design.md`; ADRs 0007–0009).
   Remaining, in order: the **holistic grill session** — all three specs on the table,
   focus on the testing spec (priming prompt:
   `agents/handover-prompts/HANDOFF-2026-08-10-testing-grill-session.md`) →
   `writing-plans` (multi-phase; phases are vertical slices co-developing `tools/`, SDK,
   Extensions, and tests — co-development per spec §3 / ADR-0006). `api-design`
   (extend-only) + `snapshot-testing` (Verify) lock the public surface as implementation
   lands (testing spec §12).
2. **Generator build-out + implementation** — `executing-plans` /
   `subagent-driven-development`; the model-layer generator per ADR-0003 and `AGENTS.md`
   (Roslyn emission; `tools/` architecture with a file-based entry; tooling stack:
   Spectre.Console.Cli + CliWrap + System.IO.Abstractions, testable via
   `Spectre.Console.Cli.Testing` — the no-CliWrap rule is SDK-product-scoped, tooling may use
   it freely); `test-driven-development` for transport/SSE/launcher;
   `csharp-concurrency-patterns` (SSE/Channels), `serialization` (source-gen STJ is decided),
   `run-tests` + `mtp-hot-reload`; `requesting-code-review` +
   `finishing-a-development-branch` at branch close. Adds the test projects per the testing
   spec §4 (`OpenCode.Sdk.Integration.Tests`; `OpenCode.Sdk.Extensions.Tests` when
   Extensions gains real code). Boundaries settled at the grill:
   generated code passes the analyzer wall on merit (ADR-0003) — settle the mechanics here
   (file naming, how the generated-code exemption is switched off, the fate of per-file
   `#nullable` directives); stream endpoints (detected by their `text/event-stream` content type) are
   wired by hand through the hand-written SSE engine; the generator emits their item
   schemas.
3. **Later** — MCP server on ModelContextProtocol.AspNetCore + stdio, in this repo (ADR-0006);
   evaluate NuGet's `McpServer` package type for distribution; its SDK usage defines the
   deep-tested legacy set (ADR-0005). "opencode HQ" — multi-instance aggregation above the
   SDK — is a valued future deliverable in its own right, not SDK scope.

## Open Questions

- **net472 spike items:** SSE behavior on long-lived responses
  (`ServicePointManager.DefaultConnectionLimit = 2` gotcha), async stdout reading,
  `taskkill /T /F` tree-kill fallback, polyfill set validation (Polyfill's companion packages —
  `System.Memory`, `Microsoft.Bcl.AsyncInterfaces` — plus latest System.Text.Json downlevel),
  and generated-model downlevel compile per generator spec §12's checklist
  (`required`/`init`/records via Polyfill; the converter-used STJ surface —
  `Utf8JsonReader`/`JsonDocument` buffering, `JsonNumberHandling` named-float literals,
  `[JsonStringEnumMemberName]`; plain-`Dictionary` dispatch maps — untested, the spike
  slices compiled net10.0 only; `[JsonPolymorphic]`/`AllowOutOfOrderMetadataProperties`
  retired by the converter design).
- **Spec tracking:** `openapi.json` changes on every upstream push — snapshot per SDK release +
  diff/regen workflow (`spec/SNAPSHOT.md` is the pin; the submodule tracks upstream).
- **Launcher deep-dive items** (spec §13): auto-port mechanics (`--port=0` support
  UNVERIFIED; `TcpListener(0)` probe fallback with bounded retry; child bind-failure
  signature detection); six-point anatomy of doc 06 §3 at implementation.
- **Release mechanics** — decided parts live in ADR-0006 (independent semver, per-merge GitHub
  Packages CD, manual NuGet.org release pipeline). Still open: pre-1.0/preview numbering,
  `VersionPrefix`, RELEASE_NOTES flow, the concrete workflows.

## Known Gaps

- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
