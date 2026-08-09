# Roadmap

Date: 2026-08-08

Operational state: what is done, what is next, what is open. This file shrinks as work lands.
Evergreen rules and locked decisions live in `../AGENTS.md`; decision records in `adr/`.

## Status

Structural skeleton in place: `OpenCode.slnx`; `src/OpenCode.Sdk` + `src/OpenCode.Sdk.Extensions`
(empty multi-targeted shells, full TFM matrix); `tests/OpenCode.Sdk.Tests` (TUnit smoke test,
net472 leg Windows-only); three-OS CI (`.github/workflows/ci.yml`: build + test + TRX reporting);
pinned OpenAPI snapshot (`spec/openapi.json`, provenance in `spec/SNAPSHOT.md`). Packages current
as of 2026-08-08. Codegen spike complete (research doc 08). Grill session complete: ADRs
0001–0006 backfilled, root `CONTEXT.md` glossary created, ADR rules canonicalized in
`adr/README.md`. Public API design complete — spec at
`superpowers/specs/2026-08-09-public-api-design.md`; research doc 10 corrected doc 09's
direction (the "2.0 branch" is an April-2026 ancestor of today's root surface, not the next
major). No SDK code yet — next up: grill the spec (handover:
`agents/handover-prompts/2026-08-09-api-design-followups.md`).

## Queue

In order — do not improvise beyond it without asking the maintainer. Parenthetical skill notes
are hints for the driving agent.

1. **Public API design — remaining steps.** The design itself is done
   (`superpowers/specs/2026-08-09-public-api-design.md` — error model, envelopes, client
   composition, naming/projection, transport, options/DI, event model, model-layer rules,
   launcher surface; every doc-08 feed-forward item resolved). Remaining, in order:
   `grilling` session over the spec (fresh context; ADR candidates in spec §15) → a
   generator-architecture design session (spec §15) → `writing-plans` (expected
   multi-phase). `api-design` (extend-only) + `snapshot-testing` (Verify) lock the public
   surface as implementation lands.
2. **Generator build-out + implementation** — `executing-plans` /
   `subagent-driven-development`; the model-layer generator per ADR-0003 and `AGENTS.md`
   (Roslyn emission; `tools/` architecture with a file-based entry; tooling stack:
   Spectre.Console.Cli + CliWrap + System.IO.Abstractions, testable via
   `Spectre.Console.Cli.Testing` — the no-CliWrap rule is SDK-product-scoped, tooling may use
   it freely); `test-driven-development` for transport/SSE/launcher;
   `csharp-concurrency-patterns` (SSE/Channels), `serialization` (source-gen STJ is decided),
   `run-tests` + `mtp-hot-reload`; `requesting-code-review` +
   `finishing-a-development-branch` at branch close. Adds the integration-test project and,
   when Extensions gains real code, its own test project. Boundaries settled at the grill:
   generated code passes the analyzer wall on merit (ADR-0003) — settle the mechanics here
   (file naming, how the generated-code exemption is switched off, the fate of per-file
   `#nullable` directives); the generator emits `x-effect-stream` SSE item schemas, but stream
   endpoints are wired by hand through the hand-written SSE engine.
3. **Later** — MCP server on ModelContextProtocol.AspNetCore + stdio, in this repo (ADR-0006);
   evaluate NuGet's `McpServer` package type for distribution; its SDK usage defines the
   deep-tested legacy set (ADR-0005). "opencode HQ" — multi-instance aggregation above the
   SDK — is a valued future deliverable in its own right, not SDK scope.

## Open Questions

- **net472 spike items:** SSE behavior on long-lived responses
  (`ServicePointManager.DefaultConnectionLimit = 2` gotcha), async stdout reading,
  `taskkill /T /F` tree-kill fallback, polyfill set validation (Polyfill's companion packages —
  `System.Memory`, `Microsoft.Bcl.AsyncInterfaces` — plus latest System.Text.Json downlevel),
  and generated-model downlevel compile (`required`, `[JsonPolymorphic]`,
  `AllowOutOfOrderMetadataProperties` via the downlevel System.Text.Json package — untested,
  the spike slice compiled net10.0 only).
- **Spec tracking:** `openapi.json` changes on every upstream push — snapshot per SDK release +
  diff/regen workflow (`spec/SNAPSHOT.md` is the pin; the submodule tracks upstream).
- **Launcher deep-dive items** (spec §13): port-conflict handling / ephemeral port
  (`--port=0` support UNVERIFIED); six-point anatomy of doc 06 §3 at implementation.
- **Release mechanics** — decided parts live in ADR-0006 (independent semver, per-merge GitHub
  Packages CD, manual NuGet.org release pipeline). Still open: pre-1.0/preview numbering,
  `VersionPrefix`, RELEASE_NOTES flow, the concrete workflows.
- **Testing strategy details** — integration/functional design against a real opencode process;
  steal upstream's "every endpoint must be exercised" idea (`test:httpapi`); legacy scope is
  consumer-driven per ADR-0005.

## Known Gaps

- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
