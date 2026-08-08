# Roadmap

Date: 2026-08-08

Operational state: what is done, what is next, what is open. This file shrinks as work lands.
Evergreen rules and locked decisions live in `../AGENTS.md`.

## Status

Pre-code. The configuration skeleton (8 root config files + README) and the analyzer policy are
in place, packages current as of 2026-08-08. No solution or project files exist yet.

## Queue

In order — do not improvise beyond it without asking Deniz. Parenthetical skill notes are hints
for the driving agent.

1. **Structural skeleton + spec pin** — `OpenCode.slnx`, `src/` + `tests/` layout, CI workflow
   (build + test; grows to the three-OS matrix when the launcher lands). Pin the current
   `openapi.json` snapshot into the repo, traceable to the submodule commit. Settle
   `AllTargetFrameworks` while the first projects land (see Parked Decisions). (Skills:
   `dotnet-project-structure`, `msbuild-antipatterns` pre-commit scan,
   `verification-before-completion`.)
2. **Codegen spike** — own generator vs Kiota/NSwag/OpenAPI Generator as the model-layer
   mechanism. Evaluate on: OpenAPI 3.1 support, discriminated-union → C# mapping,
   `JsonSerializerContext` emission, analyzer-compliance / auto-generated marking, v2-only
   filtering. Results → `docs/research/`. (Skills: `prototype`, `research`, `csharp-scripts`.)
3. **Grill session** — `grilling` / `grill-with-docs` against `AGENTS.md` + docs, spike evidence
   in hand. Side products: root `CONTEXT.md` glossary (upstream domain terms: Session, Message,
   Part, durable vs live events, Permission, …) and the first backfilled ADRs (candidates:
   launcher-in-core, v2-only surface, hybrid codegen; consider the TFM matrix. Analyzer policy is
   fully documented in research doc 07 — an ADR would just point there; optional).
4. **Public API design** — `brainstorming` (genuinely open: client surface, error model, options,
   event model) → `writing-plans`. `api-design` (extend-only) + `snapshot-testing` (Verify) lock
   the public surface as the design lands. Decides the CS1591 / XML-doc posture.
5. **Implementation** — `executing-plans` / `subagent-driven-development`;
   `test-driven-development` for transport/SSE/launcher; `csharp-concurrency-patterns`
   (SSE/Channels), `serialization` (source-gen STJ is decided), `run-tests` + `mtp-hot-reload`;
   `requesting-code-review` + `finishing-a-development-branch` at branch close.
6. **Later** — MCP server on ModelContextProtocol.AspNetCore + stdio. "opencode HQ"
   multi-instance aggregation lives above the SDK, not in it.

## Open Questions

- **net472 spike items:** SSE behavior on long-lived responses
  (`ServicePointManager.DefaultConnectionLimit = 2` gotcha), async stdout reading,
  `taskkill /T /F` tree-kill fallback, polyfill set validation
  (`Microsoft.Bcl.AsyncInterfaces`, PolySharp, latest System.Text.Json downlevel).
- **Typed event model:** SSE payloads are a large discriminated union — design the .NET
  representation (`[JsonPolymorphic]`, `AllowOutOfOrderMetadataProperties`, unknown-event
  forward compatibility).
- **`x-opencode-directory` header** — per-request project targeting: first-class option on every
  call vs client-level default + override.
- **Spec tracking:** `openapi.json` changes on every upstream push — snapshot per SDK release +
  diff/regen workflow (the submodule is today's pinning mechanism).
- **`pty.connect` WebSocket endpoints** — upstream's own codegen excludes them; probably out of
  scope for us too.
- **Auth shape** — HTTP basic (`OPENCODE_SERVER_PASSWORD`): client options vs per-request.
- **Versioning/release strategy** — `VersionPrefix`, RELEASE_NOTES flow, relationship to
  upstream versions.
- **Testing strategy details** — integration/functional design against a real opencode process;
  steal upstream's "every endpoint must be exercised" idea (`test:httpapi`).

## Parked Decisions

- **CSharpier as formatter gate** — decided in principle (mapperly pattern: `csharpier check`
  owns whitespace, IDE0055 ceded to it, `dotnet format style`/`analyzers` layered on top). Wire
  together with the first `.csproj` and the CI workflow; finalize `max_line_length` (currently
  180) and MA0051 method-size limits at the same moment.
- **CS1591 / public XML-doc enforcement** — decide in the API design session (queue item 4).
- **`AllTargetFrameworks`** — still `net10.0;net8.0;net9.0`; net472 (and the reopened
  netstandard2.0 question) settled with queue item 1. The locked TFM matrix is
  `net472;net8.0;net9.0;net10.0`.

## Known Gaps

- **`dotnet pack` is blocked:** `Directory.Build.props` references `assets/icon.png`, which does
  not exist yet (opencode logo TBD; icon content/name decision parked).
- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
