# Agent Instructions

Operating rules for LLM/code agents working in this repository. Harness-neutral: multiple
agent harnesses read this file natively — keep it free of harness-specific content. Canonical
and evergreen: it holds only what stays true. Current state, the work queue, and open
questions live in `docs/ROADMAP.md`.

## Purpose

Build a **.NET SDK for opencode** — a typed client for the HTTP API every opencode front-end
(TUI, desktop, web UI, plugins) goes through — and, on top of it, an **MCP server** exposing
opencode to any MCP client. Both live in this repository; the MCP server is by design a thin
adapter over our own SDK (ADR-0006). Ecosystem-gap evidence: research docs 03/04.

## Locked Decisions

One line per decision; rationale lives in the ADRs (`docs/adr/`), dated evidence in
`docs/research/`. **Reading the ADRs is an integral part of onboarding — do not propose or
change design without knowing them.** Not every locked decision has an ADR (ADRs are created
lazily); this list is the complete inventory of what is settled. Do not reopen without new
evidence. The design specs under `docs/superpowers/` are vision/reference material —
direction and rationale, not law; only this list and the ADRs bind.

- **Target artifact:** upstream's `packages/protocol/openapi.json` from the active `v2`
  branch (OpenAPI 3.1), pinned as a snapshot under `spec/` (ADR-0005; the 1.x pin retires
  at the M1 retarget task).
- **API surface:** the v2 protocol surface only — public names strip the `v2.` operationId
  prefix ("V2" never appears) (ADR-0005).
- **Hybrid construction:** hand-written behavior core; models *and* operation methods from
  our own Roslyn-emission generator; spec ingestion rides the pinned `Microsoft.OpenApi`
  reader — the generator owns a fail-closed semantic projection, never an OpenAPI parser;
  excluded/hand-wired operations are fingerprint-pinned (ADR-0003, ADR-0008).
- **Generator packaging:** repo tooling under `tools/`; committed output passes the analyzer
  wall on merit; the same tool owns spec refresh (ADR-0003).
- **Generated models:** immutable, `required`-mirroring, nullable-last-resort (ADR-0004).
- **Unknown-variant tolerance:** every union deserializes unknown tags into an explicit
  carrier (ADR-0009).
- **Error model:** typed exception spine carrying tagged error data; per-call `NoThrow`, no
  client-level switch (ADR-0007).
- **TFM matrix:** `netstandard2.0;net472;net8.0;net9.0;net10.0`; net11 light-up post-GA;
  ns2.0 tested by proxy via the net472 legs (ADR-0002).
- **Packages:** `OpenCode.Sdk` (core; server launcher included, hand-rolled — ADR-0001) +
  `OpenCode.Sdk.Extensions` (DI); dependency policy: research doc 06.
- **Monorepo, independent versioning:** per-merge GitHub Packages CD, manual NuGet.org
  releases (ADR-0006).
- **Licensing:** MIT (`PackageLicenseFile`).
- **SSE:** `IAsyncEnumerable<T>`, no auto-reconnect (research doc 02); the durable-stream
  resume design is re-derived against the v2 surface at M3 (`v2.session.log` carries
  `after`/`follow`).
- **`ConfigureAwait(false)`:** triple-enforced in product code, off in tests (research
  doc 07).
- **Analyzer policy:** fail-closed maximalist (research doc 07; relitigation ban: Hard
  Rules).
- **Native AOT:** source-generated System.Text.Json via a generator-emitted registry;
  `IsAotCompatible` on net10+ (ADR-0003).
- **Testing posture:** borderline-paranoid, fail-closed, defensive by default —
  observation-based gates, absolute determinism, fake only published contracts; TUnit on
  Microsoft.Testing.Platform, real-process integration, three-OS launcher acceptance
  (ADR-0001). Assurance intensity scales with blast radius: shipped SDK runtime highest;
  committed generated output next (git diff + analyzer wall + contract tests are its
  radar); repo tooling internals lightest — every extra mechanism must name a consumer or
  a concrete failure it prevents.

## Hard Rules

- **Analyzer policy is final — do not relitigate.** Redundant rules are deliberate. When a rule
  misfires on real code, the move is a per-rule arbitration comment naming the winner — pattern
  in `.editorconfig` §12 and doc 07 Part II d — never a policy rollback.
- **`LangVersion=14.0` and `AnalysisLevel=10.0` are deliberate numeric pins** — never "fix" them
  back to `latest`. C# 14 on net472 is unsupported-but-standard via polyfills (Polyfill, wired
  repo-wide; it expects a current language version, so a future C# bump is done by moving the
  pin deliberately).
- **`GenerateDocumentationFile=true` is load-bearing:** IDE0005 (unused usings) does not fire in
  CLI builds without it. Keep it and the guard comment beside it in `Directory.Build.props`.
- **Analyzer package currency is manual:** since SDK 9 nothing warns when the pinned NetAnalyzers
  package falls behind the SDK — the periodic bump routine owns it.
- `external/` submodules belong to upstream; never hand-edit them.

## Working Agreements

- All repo artifacts are written in English.
- Everything temporary — prototypes, scratch scripts, working notes — lives under the root
  `.scratchpad/` directory, which is fully gitignored. Nothing permanent lives there and no
  permanent artifact references it; validated results are canonicalized into `docs/` or code
  once settled. Keep a minimal `Directory.Build.props` stub (empty `<Project>` that also turns
  central package management off) at the `.scratchpad/` root so scratch projects do not inherit
  the repo's strict build infrastructure.
- Be direct, practical, and clear. Challenge decisions when needed — argue from mechanisms and
  sources, not convention; do not yes-person your way into bad architecture. A well-grounded
  "no" will be accepted.
- *"No plan survives first contact with the enemy."* — Helmuth von Moltke. Specs and plans are
  falsifiable instruments, not law: when implementation contradicts a sealed decision, stop
  and follow the deviation protocol (`docs/agents/deviation-protocol.md`) — never silently
  code around it.
- A question wants an answer, not an action. When something is ambiguous, lay out options with a
  recommendation and ask — don't resolve it silently.
- Align before writing: propose structure/plan first, get an OK, then write.
- Prefer small correct changes over broad refactors.
- Verify before claiming: "builds" and "works" are different words — run build, tests, and the
  format gate before reporting done.
- Run the local Slopwatch gate as `dotnet tool run slopwatch analyze --exclude
  ".scratchpad/**,external/**" --fail-on warning`; local throwaway work and checked-out upstream
  submodules are outside the repository-authored code surface.
- A docs-only follow-up commit to an already-green mixed PR uses `[skip ci]` only when every
  change since the tested commit is Markdown or `LICENSE`. Never skip CI for source, tests,
  project/build files, tool manifests or baselines, workflow files, or generated artifacts.
- Research/decision sessions end with a documentation pass (research log in
  question→finding→decision format, topic docs, ROADMAP) and a single commit.
- Commit messages follow Conventional Commits (`feat`, `fix`, `docs`, `test`, `refactor`,
  `build`, `ci`, `chore`); no AI attribution trailers.
- Commit only with the maintainer's approval — except inside an explicitly agreed development
  loop, where committing is part of the flow.

## Engineering Conventions

- **Defensive programming is the default, everywhere:** guard public inputs, assert internal
  invariants, fail loudly rather than guess — silent fallbacks exist only as explicitly
  recorded tolerances (ADR-0009 pattern).
- **Code style is canonical in `docs/engineering/coding-style.md`:** named collaborator
  classes over private-method accumulation; interfaces at seams with full-battery DI-first
  executable composition, sealed everywhere else; no tuple returns or concrete-collection
  parameters across class boundaries; vertical feature-slice layout with conventional groups.
- **Test authorship is canonical in `docs/engineering/testing-style.md`:** test
  infrastructure is first-class (central scenario assembly, domain-aware fluent builders;
  named scenario classes promoted only on reuse/complexity/domain identity); test data lives
  in embedded fixtures, typed builders, or centralized constants — never as inline dumps in
  test bodies; Testably supplies the repository's shared `IFileSystem` seam and canonical fake.
- **Test naming:** `{Symbol}_Should_{Expected_Behavior}[_When_{Condition}]`. Symbol names stay
  intact as one token (`TryResolve`, `NuGet`); every other word is `_`-separated and starts with
  a capital. Example: `TryResolve_Should_Return_False_When_Routes_Are_Invalid`. Test classes are
  `{Sut}Tests` in a single file by default; promote a SUT to a folder with per-method test files
  only when the class outgrows comfortable navigation.

## Sources of Truth

Read change-prone facts from their source files instead of copying them into docs.

| Fact | Source |
|---|---|
| Purpose, locked decisions, rules | `AGENTS.md` (this file) |
| Decision rationale, dated research | `docs/research/` |
| Status, queue, open questions, known gaps | `docs/ROADMAP.md` (operational — expected to change) |
| Architecture decisions | `docs/adr/` (criteria & format: its `README.md`) |
| Domain glossary | `CONTEXT.md` (`external/opencode` has its own — no clash) |
| Engineering style (code & test authorship) | `docs/engineering/` |
| Pinned OpenAPI spec + provenance | `spec/` (`SNAPSHOT.md` records commit/tag and refresh steps) |
| Agent-only config (issue tracker, triage labels, domain-doc rules) | `docs/agents/` |
| Session handovers | `docs/agents/handover-prompts/` |

## Documentation Hygiene

Evergreen (`AGENTS.md`, `docs/adr/`, `docs/research/`) answers "how it works and why".
Operational (`docs/ROADMAP.md`) answers "what is done, what is next" and shrinks as work lands. A
sentence that needs rewriting when a task completes is operational. Keep documentation current in
the same change as the code it describes — docs are a first-class citizen, not follow-up work.
Documents describe the status quo, not their own
history — no amendment notes; git carries history. Every hand-written document under `docs/`
carries a `Date:` line.

Every fact has exactly one canonical home; any other appearance is a relay that links there
instead of restating it. Audience decides placement: `docs/agents/` holds guidance only an AI
agent needs; knowledge shared by humans and agents lives in `docs/research/`, `docs/adr/`, and
`docs/ROADMAP.md`.

References point one way only: docs may cite code, **code never cites docs**. Comments in code
artifacts (source, project files, `.editorconfig`, workflows) explain the status quo on the spot
— never "see docs/…", never decision history. Docs move and renumber; a stale pointer baked into
code is worse than none.

ADRs are created lazily; criteria and format live in `docs/adr/README.md`. Handovers track
unfinished cross-session state: consume them against live git and delete them when the
follow-up ships.
