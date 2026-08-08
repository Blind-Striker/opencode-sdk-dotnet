# Agent Instructions

Operating rules for LLM/code agents working in this repository. Canonical and evergreen: it holds
only what stays true. Current state, the work queue, and open questions live in `docs/ROADMAP.md`.

## Purpose

Build a **.NET SDK for opencode** — a typed client for the HTTP API that every opencode front-end
(TUI, desktop, web UI, plugins) already goes through — and, on top of it, an **MCP server**
exposing opencode to any MCP client. The niche is real: the only maintained first-party SDK is
JS/TypeScript, the Stainless-generated Python and Go SDKs are effectively abandoned, there is no
.NET SDK at all, and the one community MCP bridge is architecturally fragile and unmaintained.
Evidence: `docs/research/04-ecosystem.md` and `docs/research/03-opencode-mcp-assessment.md`.

Ship order: **SDK first, MCP server second** — the MCP server becomes a thin adapter over our own
SDK, avoiding the private-internals trap that sank the unofficial `opencode-mcp`.

## Locked Decisions

Decision statements live here until ADRs are backfilled (grill session); rationale lives in the
linked research docs. Do not reopen these without new evidence.

- **Target artifact:** `external/opencode/packages/sdk/openapi.json` — the committed,
  CI-regenerated OpenAPI 3.1 spec (162 paths, 472 schemas at opencode 1.18.15); the same spec the
  official JS SDK is generated from.
- **v2 surface only** (`/api/*` routes, `v2.` operation IDs). Upstream's stability commitments
  are phrased about v2 (`docs/research/02-sdk-next-and-http-stability.md`).
- **Hybrid construction** (`docs/research/06-dotnet-sdk-design.md` §1): hand-written core
  (transport pipeline, SSE engine, process lifecycle, DI, error model, public API) plus a
  mechanically derived model layer — 472 schemas are never hand-maintained. Preferred mechanism
  is our own generator; Kiota/NSwag serve as spike benchmarks.
- **TFM matrix:** `net472;net8.0;net9.0;net10.0`; `net11.0` light-up planned post-GA
  (2026-11-10), mainly for the new Process APIs. net472 gives .NET Framework reach and superseded
  the original netstandard2.0 idea (that question is reopened — see `docs/ROADMAP.md`).
- **Packages:**
  - `OpenCode.Sdk` — core. Deps: System.Text.Json, System.Net.ServerSentEvents,
    Microsoft.Extensions.Logging.Abstractions + downlevel polyfills. **Includes the server
    launcher**, hand-rolled on `System.Diagnostics.Process`, no CliWrap (upstream parity: the JS
    SDK ships `createOpencodeServer()` in-package; the MCP C# SDK spawns processes in core —
    `StdioClientTransport` is the reference implementation). HttpClient injectable via
    constructor.
  - `OpenCode.Sdk.Extensions` — Microsoft.Extensions.Http + DI.Abstractions + Options;
    `AddOpenCodeClient()`, IHttpClientFactory wiring, options binding.
  - Future candidate: `OpenCode.Aspire.Hosting`.
  - NuGet ID `OpenCode.Sdk` verified available (2026-08-08). Solution file: `OpenCode.slnx`.
    README carries an explicit "unofficial" note.
- **Licensing/packaging:** MIT via a packed `LICENSE` copy (`PackageLicenseFile`);
  `PackageLicenseExpression` was considered and rejected.
- **SSE as `IAsyncEnumerable<T>`**, no automatic reconnect — matches upstream design; the
  durable per-session stream resumes via the `after` cursor.
- **`ConfigureAwait(false)` mandatory** (net472 in the matrix ⇒ SynchronizationContext deadlocks
  are real). Triple-enforced: CA2007 + MA0004 (`report=Always`) + VSTHRD111, all `error` in
  product code, all `none` for tests (`.editorconfig` §15).
- **Analyzer policy: fail-closed maximalist** — `AnalysisMode=All` + unconditional
  TreatWarningsAsErrors/CodeAnalysisTreatWarningsAsErrors, deliberately against the community
  norm; new rules must break the build and force a recorded decision. Rationale and decision
  table (D1–D9): `docs/research/07-analyzer-policy.md`.
- **Native AOT friendly:** source-generated System.Text.Json (`JsonSerializerContext`; the
  `[JsonSerializable]` list is generator-emitted), `IsAotCompatible=true` on net10+.
- **Aspire stays** — planned local dev/test AppHost (mini UI, `opencode serve` as a resource);
  the core OTel packages support that host.
- **Testing:** TUnit on Microsoft.Testing.Platform; unit plus integration/functional tests
  against a real opencode process. Launcher acceptance criterion: three-OS CI matrix
  (Windows/Linux/macOS) with real `opencode serve` start/stop tests.
- **MCP server targets the 2026-07-28 spec** via MCP C# SDK v2.0 (stdio + streamable HTTP); no
  investment in deprecated features (Sampling/Roots/Logging, HTTP+SSE transport).

## Hard Rules

- **Analyzer policy is final — do not relitigate.** Redundant rules are deliberate. When a rule
  misfires on real code, the move is a per-rule arbitration comment naming the winner — pattern
  in `.editorconfig` §12 and doc 07 Part II d — never a policy rollback.
- **`LangVersion=14.0` and `AnalysisLevel=10.0` are deliberate numeric pins** — never "fix" them
  back to `latest`. C# 14 on net472 is unsupported-but-standard via polyfills (PolySharp
  planned).
- **`GenerateDocumentationFile=true` is load-bearing:** IDE0005 (unused usings) does not fire in
  CLI builds without it. Keep it and the guard comment beside it in `Directory.Build.props`.
- **Analyzer package currency is manual:** since SDK 9 nothing warns when the pinned NetAnalyzers
  package falls behind the SDK — the periodic bump routine owns it.
- `external/` submodules belong to upstream; never hand-edit them.

## Working Agreements

- Conversation with Deniz in Turkish; **all repo artifacts in English**.
- Align before writing: propose structure/plan first, get an OK, then write.
- Research/decision sessions end with a documentation pass (research log in
  question→finding→decision format, topic docs, ROADMAP) and a single commit.
- Commit only when Deniz says so; direct commits on `master`; trailer:
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Evidence-based pushback is invited — argue from mechanisms and sources, not convention; a
  well-grounded "no" will be accepted.

## Sources of Truth

| Fact | Source |
|---|---|
| Purpose, locked decisions, rules | `AGENTS.md` (this file) |
| Decision rationale, dated research | `docs/research/` |
| Status, queue, open questions, known gaps | `docs/ROADMAP.md` (operational — expected to change) |
| Architecture decisions | `docs/adr/` (lazy — backfilled from the grill session on) |
| Domain glossary | `CONTEXT.md` (created lazily; `external/opencode` has its own — no clash) |
| Agent-only config (issue tracker, triage labels, domain-doc rules) | `docs/agents/` |
| Session handovers | `docs/agents/handover-prompts/` |

## Documentation Hygiene

Evergreen (`AGENTS.md`, `docs/adr/`, `docs/research/`) answers "how it works and why".
Operational (`docs/ROADMAP.md`) answers "what is done, what is next" and shrinks as work lands. A
sentence that needs rewriting when a task completes is operational. Keep documentation current in
the same change as the code it describes. Documents describe the status quo, not their own
history — no amendment notes; git carries history. Every hand-written document under `docs/`
carries a `Date:` line.

Every fact has exactly one canonical home; any other appearance is a relay that links there
instead of restating it. Audience decides placement: `docs/agents/` holds guidance only an AI
agent needs; knowledge shared by humans and agents lives in `docs/research/`, `docs/adr/`, and
`docs/ROADMAP.md`.

ADRs are created lazily, and only when all three hold: hard to reverse, surprising, and a real
trade-off. Handovers track unfinished cross-session state: consume them against live git and
delete them when the follow-up ships.

## Harness Independence

`AGENTS.md` is the canonical contract; `CLAUDE.md` is a relay only. opencode reads `AGENTS.md`
natively — keep this file short and harness-neutral.

## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues (`gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary; label strings equal role names. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: root `CONTEXT.md` + `docs/adr/`, both created lazily. See
`docs/agents/domain.md`.
