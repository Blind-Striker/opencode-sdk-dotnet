# Agent Instructions

Operating rules for LLM/code agents working in this repository. Canonical and evergreen: it holds
only what stays true. Current state, the work queue, and open questions live in `docs/ROADMAP.md`.

## Purpose

Build a **.NET SDK for opencode** — a typed client for the HTTP API that every opencode front-end
(TUI, desktop, web UI, plugins) already goes through — and, on top of it, an **MCP server**
exposing opencode to any MCP client — both deliverables live in this repository (ADR-0006 in
`docs/adr/`). The niche is real: the only maintained first-party SDK is
JS/TypeScript, the Stainless-generated Python and Go SDKs are effectively abandoned, there is no
.NET SDK at all, and the one community MCP bridge is architecturally fragile and unmaintained.
Evidence: `docs/research/04-ecosystem.md` and `docs/research/03-opencode-mcp-assessment.md`.

Ship order: **SDK first, MCP server second** — the MCP server becomes a thin adapter over our own
SDK, avoiding the private-internals trap that sank the unofficial `opencode-mcp`.

## Locked Decisions

Decision statements only — rationale lives in the ADRs (`docs/adr/`), evidence in the research
docs. Do not reopen these without new evidence.

- **Target artifact:** `external/opencode/packages/sdk/openapi.json` (OpenAPI 3.1) — the same
  spec the official JS SDK is generated from. Pinned copy: `spec/`.
- **API surface: both generations of the pinned 1.x spec** — the transitional modern block
  (`v2.*` operationIds; 61 ops today, 3 outside `/api/*`) and the legacy block (127 ops). The
  modern surface takes the unmarked public names (the `v2.` prefix is stripped and "V2" never
  appears in type or client names); the legacy surface lives behind an explicitly
  legacy-marked sub-surface. Deep integration testing targets the modern surface plus every
  legacy operation the MCP server consumes; remaining legacy is best-effort. The legacy area
  is deleted at a major release of ours when upstream ships its next major (ADR-0005;
  research docs 08, 09, 10).
- **Hybrid construction:** hand-written behavior core (transport pipeline, SSE engine,
  process lifecycle, DI, error model, envelope/options bases), mechanically derived
  everything else — models **and operation methods** — from **our own generator**: Roslyn
  syntax-tree emission over a parsed spec IR; scope-agnostic (surface selection is a
  filter parameter); excluded/hand-wired operations are fingerprint-pinned (ADR-0003,
  ADR-0008; research docs 06 §1, 08).
- **Generator packaging:** repo tooling under `tools/` — csproj bound to the repo build
  rules, thin file-based `.cs` entry committed with the executable bit. Generated output is
  committed into the SDK project and passes the analyzer wall **on merit** — no blanket
  generated-code exemption (file-naming mechanics settled at build-out); the tool runs
  `dotnet format` on its output as a post-step; CI regen-verifies. The same tool owns spec
  refresh: submodule pin bump, `spec/` copy, `SNAPSHOT.md` stamp (ADR-0003).
- **Model-layer generation principles:** immutable by default (records, `init`-only,
  read-only collections); `required` where the spec requires; nullable is a last resort —
  absent collections emit empty, nullable only where absence carries meaning (ADR-0004).
- **TFM matrix:** `netstandard2.0;net472;net8.0;net9.0;net10.0`; `net11.0` light-up post-GA
  (2026-11-10). net472 = Framework-exact compile paths; ns2.0 = extra reach on the same polyfill
  tax, tested by proxy via the net472 leg (ADR-0002).
- **Packages:** `OpenCode.Sdk` — core (System.Text.Json, System.Net.ServerSentEvents,
  Microsoft.Extensions.Logging.Abstractions + downlevel polyfills; **server launcher included**,
  hand-rolled on `System.Diagnostics.Process`, no CliWrap (ADR-0001); HttpClient injectable) and
  `OpenCode.Sdk.Extensions` (Microsoft.Extensions.Http + DI.Abstractions + Options,
  `AddOpenCodeClient()`). Future candidate: `OpenCode.Aspire.Hosting`. NuGet ID verified
  available 2026-08-08; upstream-parity rationale: research doc 06.
- **Monorepo, independent versioning:** the MCP server is developed in this repository as a
  thin adapter over the SDK; every package versions independently — no lockstep, no alignment
  with upstream opencode versions; per-merge CD to GitHub Packages, manual NuGet.org release
  pipeline (ADR-0006).
- **Licensing:** MIT via a packed `LICENSE` copy (`PackageLicenseFile`).
- **SSE as `IAsyncEnumerable<T>`**, no automatic reconnect; the durable per-session stream
  resumes via the `after` cursor.
- **`ConfigureAwait(false)` mandatory** — triple-enforced in product code, off in tests.
- **Analyzer policy: fail-closed maximalist** — new rules must break the build and force a
  recorded decision. Rationale and decision table (D1–D9): research doc 07.
- **Native AOT friendly:** source-generated System.Text.Json (the `[JsonSerializable]` registry
  is generator-emitted); `IsAotCompatible` on net10+.
- **Aspire stays** — planned local dev/test AppHost (mini UI, `opencode serve` as a resource).
- **Testing:** TUnit on Microsoft.Testing.Platform; unit plus integration tests against a real
  opencode process. Launcher acceptance: three-OS CI with real `opencode serve` start/stop
  tests.
- **MCP server:** targets the 2026-07-28 spec via MCP C# SDK v2.0 (stdio + streamable HTTP); no
  investment in deprecated features.

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

## Harness Independence

`AGENTS.md` is the canonical contract; `CLAUDE.md` is a relay only. opencode reads `AGENTS.md`
natively — keep this file short and harness-neutral.

## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues (`gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary; label strings equal role names. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: root `CONTEXT.md` + `docs/adr/`. See `docs/agents/domain.md`.
