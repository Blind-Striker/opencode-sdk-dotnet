# Roadmap

Date: 2026-08-08

Operational state: what is done, what is next, what is open. This file shrinks as work lands.
Evergreen rules and locked decisions live in `../AGENTS.md`.

## Status

Structural skeleton in place: `OpenCode.slnx`; `src/OpenCode.Sdk` + `src/OpenCode.Sdk.Extensions`
(empty multi-targeted shells, full TFM matrix); `tests/OpenCode.Sdk.Tests` (TUnit smoke test,
net472 leg Windows-only); three-OS CI (`.github/workflows/ci.yml`: build + test + TRX reporting);
pinned OpenAPI snapshot (`spec/openapi.json`, provenance in `spec/SNAPSHOT.md`). Packages current
as of 2026-08-08. No SDK code yet — next up is the codegen spike.

## Queue

In order — do not improvise beyond it without asking the maintainer. Parenthetical skill notes
are hints for the driving agent.

1. **Codegen spike** — own generator vs Kiota/NSwag/OpenAPI Generator as the model-layer
   mechanism. Evaluate on: OpenAPI 3.1 support, discriminated-union → C# mapping,
   `JsonSerializerContext` emission, analyzer-compliance / auto-generated marking, v2-only
   filtering. Results → `docs/research/`. (Skills: `prototype`, `research`, `csharp-scripts`.)
2. **Grill session** — `grilling` / `grill-with-docs` against `AGENTS.md` + docs, spike evidence
   in hand. Side products: root `CONTEXT.md` glossary (upstream domain terms: Session, Message,
   Part, durable vs live events, Permission, …) and the first backfilled ADRs (candidates:
   launcher-in-core, v2-only surface, hybrid codegen, one-way doc references (code never cites
   docs), test naming convention; consider the TFM matrix. Analyzer policy is fully documented in
   research doc 07 — an ADR would just point there; optional).
3. **Public API design** — `brainstorming` (genuinely open: client surface, error model, options,
   event model) → `writing-plans`. `api-design` (extend-only) + `snapshot-testing` (Verify) lock
   the public surface as the design lands. Decides the CS1591 / XML-doc posture.
4. **Implementation** — `executing-plans` / `subagent-driven-development`;
   `test-driven-development` for transport/SSE/launcher; `csharp-concurrency-patterns`
   (SSE/Channels), `serialization` (source-gen STJ is decided), `run-tests` + `mtp-hot-reload`;
   `requesting-code-review` + `finishing-a-development-branch` at branch close. Adds the
   integration-test project and, when Extensions gains real code, its own test project.
5. **Later** — MCP server on ModelContextProtocol.AspNetCore + stdio. "opencode HQ"
   multi-instance aggregation lives above the SDK, not in it.

## Open Questions

- **Repo tooling architecture (TBD):** all repo tooling as .NET 10 file-based apps
  (`dotnet run tool.cs`) acting as thin entries around a `tools/` project carrying
  Spectre.Console.Cli + CliWrap + System.IO.Abstractions (PathSmith pattern — testable via
  `Spectre.Console.Cli.Testing`). First candidate: spec-refresh tool (copy `openapi.json` from
  the submodule, stamp `spec/SNAPSHOT.md`, report the diff). Note: the no-CliWrap rule is scoped
  to the SDK product (launcher); repo tooling may use CliWrap freely.
- **net472 spike items:** SSE behavior on long-lived responses
  (`ServicePointManager.DefaultConnectionLimit = 2` gotcha), async stdout reading,
  `taskkill /T /F` tree-kill fallback, polyfill set validation (Polyfill's companion packages —
  `System.Memory`, `Microsoft.Bcl.AsyncInterfaces` — plus latest System.Text.Json downlevel).
- **Typed event model:** SSE payloads are a large discriminated union — design the .NET
  representation (`[JsonPolymorphic]`, `AllowOutOfOrderMetadataProperties`, unknown-event
  forward compatibility).
- **`x-opencode-directory` header** — per-request project targeting: first-class option on every
  call vs client-level default + override.
- **Spec tracking:** `openapi.json` changes on every upstream push — snapshot per SDK release +
  diff/regen workflow (`spec/SNAPSHOT.md` is the pin; the submodule tracks upstream).
- **`pty.connect` WebSocket endpoints** — upstream's own codegen excludes them; probably out of
  scope for us too.
- **Auth shape** — HTTP basic (`OPENCODE_SERVER_PASSWORD`): client options vs per-request.
- **Versioning/release strategy** — `VersionPrefix`, RELEASE_NOTES flow, relationship to
  upstream versions; release workflow (NuGet publish) comes with this decision.
- **Testing strategy details** — integration/functional design against a real opencode process;
  steal upstream's "every endpoint must be exercised" idea (`test:httpapi`).

## Parked Decisions

- **CS1591 / public XML-doc enforcement** — decide in the API design session (queue item 3).

## Known Gaps

- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
