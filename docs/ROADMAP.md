# Roadmap

Date: 2026-08-08

Operational state: what is done, what is next, what is open. This file shrinks as work lands.
Evergreen rules and locked decisions live in `../AGENTS.md`.

## Status

Structural skeleton in place: `OpenCode.slnx`; `src/OpenCode.Sdk` + `src/OpenCode.Sdk.Extensions`
(empty multi-targeted shells, full TFM matrix); `tests/OpenCode.Sdk.Tests` (TUnit smoke test,
net472 leg Windows-only); three-OS CI (`.github/workflows/ci.yml`: build + test + TRX reporting);
pinned OpenAPI snapshot (`spec/openapi.json`, provenance in `spec/SNAPSHOT.md`). Packages current
as of 2026-08-08. Codegen spike complete (`docs/research/08-codegen-spike.md`): model layer comes
from our own generator — Roslyn syntax-tree emission, repo tooling under `tools/`, committed
output with CI regen-verify; Kiota/NSwag/OpenAPI Generator eliminated on run evidence. No SDK
code yet — next up is the grill session.

## Queue

In order — do not improvise beyond it without asking the maintainer. Parenthetical skill notes
are hints for the driving agent.

1. **Grill session** — `grilling` / `grill-with-docs` against `AGENTS.md` + docs, spike evidence
   in hand (research doc 08). Side products: root `CONTEXT.md` glossary (upstream domain terms:
   Session, Message, Part, durable vs live events, Permission, …) and the first backfilled ADRs
   (candidates: launcher-in-core, v2-only surface, codegen mechanism/emission/packaging with the
   reversal triggers from doc 08, one-way doc references (code never cites docs), test naming
   convention; consider the TFM matrix. Analyzer policy is fully documented in research doc 07 —
   an ADR would just point there; optional).
2. **Public API design** — `brainstorming` (genuinely open: client surface, error model
   including Result-pattern vs exceptions for expected failures, options, event model) →
   `writing-plans`. `api-design` (extend-only) + `snapshot-testing` (Verify) lock
   the public surface as the design lands. Decides the CS1591 / XML-doc posture, plus the
   model-layer feed-forward from doc 08: unknown-discriminator forward compatibility for the
   `V2Event` SSE union, `Uri` vs `string`, acronym casing, `WhenWritingNull`, on-merit style
   conformance vs generated-code exemption, and client-surface naming/shape for the legacy vs
   `v2.*` operations (v2 first-class; legacy best-effort — calibrate legacy test investment
   against which operations the MCP server actually uses; full 61 → 112 rename mapping vs the
   2.0 spec belongs here too).
3. **Generator build-out + implementation** — `executing-plans` /
   `subagent-driven-development`; the model-layer generator per doc 08 and `AGENTS.md`
   (Roslyn emission; `tools/` architecture with a file-based entry; tooling stack:
   Spectre.Console.Cli + CliWrap + System.IO.Abstractions, testable via
   `Spectre.Console.Cli.Testing` — the no-CliWrap rule is SDK-product-scoped, tooling may use
   it freely); `test-driven-development` for
   transport/SSE/launcher; `csharp-concurrency-patterns` (SSE/Channels), `serialization`
   (source-gen STJ is decided), `run-tests` + `mtp-hot-reload`; `requesting-code-review` +
   `finishing-a-development-branch` at branch close. Adds the integration-test project and,
   when Extensions gains real code, its own test project.
4. **Later** — MCP server on ModelContextProtocol.AspNetCore + stdio. "opencode HQ"
   multi-instance aggregation lives above the SDK, not in it.

## Open Questions

- **net472 spike items:** SSE behavior on long-lived responses
  (`ServicePointManager.DefaultConnectionLimit = 2` gotcha), async stdout reading,
  `taskkill /T /F` tree-kill fallback, polyfill set validation (Polyfill's companion packages —
  `System.Memory`, `Microsoft.Bcl.AsyncInterfaces` — plus latest System.Text.Json downlevel),
  and generated-model downlevel compile (`required`, `[JsonPolymorphic]`,
  `AllowOutOfOrderMetadataProperties` via the downlevel System.Text.Json package — untested,
  the spike slice compiled net10.0 only).
- **Typed event model:** SSE payloads are a large discriminated union — design the .NET
  representation. Spike evidence (research doc 08): `[JsonPolymorphic]` name-based dispatch
  works on the spec's literal convention (with `AllowOutOfOrderMetadataProperties`); unknown
  discriminators throw by default, so the forward-compatibility strategy is the open part.
- **`x-opencode-directory` header** — per-request project targeting: first-class option on every
  call vs client-level default + override.
- **Spec tracking:** `openapi.json` changes on every upstream push — snapshot per SDK release +
  diff/regen workflow (`spec/SNAPSHOT.md` is the pin; the submodule tracks upstream).
- **`pty.connect` WebSocket endpoints** — upstream's own codegen excludes them; probably out of
  scope for us too.
- **Auth shape** — HTTP basic (`OPENCODE_SERVER_PASSWORD`): client options vs per-request.
- **Versioning/release strategy** — decided: independent semver, NOT aligned with upstream
  opencode versions (alignment was weighed and rejected: it would force our own features onto
  patch releases; the opencode-2.0 rename wave is handled as an explicit breaking major
  instead). Still open: pre-1.0/preview numbering, `VersionPrefix`, RELEASE_NOTES flow,
  release workflow (NuGet publish).
- **Testing strategy details** — integration/functional design against a real opencode process;
  steal upstream's "every endpoint must be exercised" idea (`test:httpapi`).

## Parked Decisions

- **CS1591 / public XML-doc enforcement** — decide in the API design session (queue item 3).

## Known Gaps

- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
