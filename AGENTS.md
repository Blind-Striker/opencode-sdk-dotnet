# Agent Instructions

Harness-neutral entry point for LLM/code agents working in this repository. Keep this file focused
on rules that apply before task-specific context is known; detailed canon is loaded through the
routing table below. Current status, queue, and open questions live in `docs/ROADMAP.md`.

## Purpose

Build a typed .NET SDK for the opencode HTTP API and, in the same repository, an MCP server that is
a thin adapter over that SDK.

## Universal Rules

- Repository-authored artifacts are written in English. Temporary work lives only under
  `.scratchpad/`; validated outcomes move into code or canonical documentation.
- Upstream submodules under `external/` are read-only evidence. Never hand-edit them.
- Generated SDK output is changed through the repository generator, never by hand.
- Be direct and practical. Challenge decisions with mechanisms and sources rather than convention.
- A question asks for an answer, not an unrequested mutation. When intent is ambiguous, present the
  options, recommend one, and ask.
- Align on structure and direction before writing. Prefer the smallest correct change.
- If implementation contradicts current canon, stop the affected work and follow
  `docs/engineering/deviation-protocol.md`; never silently code around it or reopen a decision for
  convenience.
- Documentation refactors preserve useful, unique information. Relocate it to the right canonical,
  rationale, evidence, or operational home; do not discard it merely to shorten a file.
- Keep affected documentation current in the same change as code. Every current fact has one
  canonical owner; other mentions are brief relays.
- Before reporting completion, run the applicable gate in
  `docs/engineering/quality-gates.md`. "Builds" and "works" are different claims.
- Commit only with maintainer approval, except inside an explicitly agreed development loop.

Full collaboration, scratchpad, commit, and CI agreements live in
`docs/engineering/workflow.md`. Documentation ownership and lifecycle rules live in
`docs/engineering/documentation.md`.

## Task Routing

Read the canon relevant to the work before proposing or changing design. ADRs explain why a
decision exists; dated research supplies evidence and may intentionally contain superseded
positions.

| Task | Read first |
|---|---|
| Protocol, spec ingestion, curation, generator, generated models or operations | `docs/architecture/protocol-and-generation.md` plus relevant ADRs |
| Client construction, transport, errors, SSE, DI or launcher | `docs/architecture/client-runtime.md` plus relevant ADRs |
| Target frameworks, packages, dependencies, versioning, release or licensing | `docs/architecture/platform-and-packaging.md` plus relevant ADRs |
| Hand-written code structure, signatures, DI composition or layout | `docs/engineering/coding-style.md` |
| Test design or test code | `docs/engineering/testing-style.md` |
| Analyzer, build, formatting, performance or completion gates | `docs/engineering/quality-gates.md` |
| Documentation, research, ADRs or repository workflow | `docs/engineering/documentation.md`, `docs/engineering/workflow.md`, and `docs/adr/README.md` as applicable |
| Domain terminology | `CONTEXT.md` |
| Current status, queue, open questions or known gaps | `docs/ROADMAP.md`; any active session handoff lives in the private companion repository |
| Pinned protocol identity or refresh | `spec/SNAPSHOT.md` |

## Authority

- `docs/architecture/` and `docs/engineering/` own current normative project rules.
- `docs/adr/` records accepted decision context, trade-offs, consequences, and reversal triggers.
- `docs/ROADMAP.md` is operational and expected to change and shrink as work lands.
- `CONTEXT.md` owns current domain vocabulary.
- `spec/SNAPSHOT.md` owns the exact OpenAPI pin and refresh procedure.
- Dated research evidence, plans and specs, and session handovers live in the private companion
  repository `Blind-Striker/opencode-sdk-dotnet-internal`. That material is non-normative history
  and reference; contradicting it does not override current canon, and the canon here is complete
  without it.

Repository files and current canon beat memory, transient plans, dated research, and handoffs. If
two current canonical sources appear to disagree, do not choose one silently; use the deviation
protocol.
