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
`docs/research/`. Do not reopen these without new evidence. Fine-grained design currently
lives in the sealed design specs (transient; reachable via `docs/ROADMAP.md`) until
build-out distills them.

- **Target artifact:** upstream's `packages/sdk/openapi.json` (OpenAPI 3.1), pinned under
  `spec/` — the same spec the official JS SDK generates from.
- **API surface:** both generations of the pinned 1.x spec — the modern block takes the
  unmarked public names ("V2" never appears); legacy lives behind a legacy-marked
  sub-surface, deleted at our upstream-absorbing major; legacy deep-testing is
  consumer-driven (ADR-0005).
- **Hybrid construction:** hand-written behavior core; models *and* operation methods from
  our own Roslyn-emission generator; excluded/hand-wired operations are fingerprint-pinned
  (ADR-0003, ADR-0008).
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
- **SSE:** `IAsyncEnumerable<T>`, no auto-reconnect; the durable stream resumes via the
  `after` cursor (research doc 02).
- **`ConfigureAwait(false)`:** triple-enforced in product code, off in tests (research
  doc 07).
- **Analyzer policy:** fail-closed maximalist (research doc 07; relitigation ban: Hard
  Rules).
- **Native AOT:** source-generated System.Text.Json via a generator-emitted registry;
  `IsAotCompatible` on net10+ (ADR-0003).
- **Testing posture:** borderline-paranoid, fail-closed, defensive by default —
  observation-based gates, absolute determinism, fake only published contracts; TUnit on
  Microsoft.Testing.Platform, real-process integration, three-OS launcher acceptance
  (ADR-0001).
- **MCP server:** targets the 2026-07-28 spec via MCP C# SDK v2.0, stdio + streamable HTTP
  (research doc 05).

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
- A question wants an answer, not an action. When something is ambiguous, lay out options with a
  recommendation and ask — don't resolve it silently.
- Align before writing: propose structure/plan first, get an OK, then write.
- Prefer small correct changes over broad refactors.
- Verify before claiming: "builds" and "works" are different words — run build, tests, and the
  format gate before reporting done.
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
