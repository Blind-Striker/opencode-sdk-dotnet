# GOAL

> Status: draft / unstructured bucket. This file intentionally mixes goals, decisions,
> open questions, and TODOs while the project takes shape. Expect it to be messy;
> promote things out of here when they deserve their own document.

## Purpose

Build a **.NET SDK for opencode** — a typed client for the HTTP API that every opencode
front-end (TUI, desktop, web UI, plugins) already goes through — and, on top of it, an
**MCP server** exposing opencode to any MCP client.

Why this is worth doing (see `docs/research/04-ecosystem.md` for evidence):

- The only first-class, current opencode SDK is JS/TypeScript (`@opencode-ai/sdk`).
- The Stainless-generated Python and Go SDKs are effectively abandoned (bot-only
  commits, frozen since 2025-08/2025-12, far behind the current API).
- There is no .NET SDK at all, official or community.
- The one existing MCP bridge (`opencode-mcp`, unofficial) is architecturally fragile
  and unmaintained since 2026-05 (see `docs/research/03-opencode-mcp-assessment.md`).
- Upstream has explicitly committed to keeping the HTTP surface stable through the
  v2 / sdk-next transition (see `docs/research/02-sdk-next-and-http-stability.md`).

## Decisions (so far)

- **Target artifact:** `external/opencode/packages/sdk/openapi.json` — the committed,
  CI-regenerated OpenAPI 3.1 spec (162 paths, 472 schemas at opencode 1.18.15). This is
  the same spec the official JS SDK is generated from.
- **Roadmap order: SDK first, MCP server second.** The MCP server becomes a thin
  adapter over our own SDK (each tool ≈ one SDK call + formatting). This avoids the
  trap the unofficial `opencode-mcp` fell into: no HTTP layer of its own, so it reaches
  into the JS SDK's private `_client` field.
- **SSE is exposed as `IAsyncEnumerable<T>`** with `CancellationToken`, **no automatic
  reconnect** — matching upstream's own design decision (streams fail explicitly;
  consumers refresh state and resubscribe).
- **MCP server targets the 2026-07-28 spec** via the official MCP C# SDK v2.0
  (stateless HTTP + stdio). Do not invest in features deprecated by that revision
  (Sampling, Roots, Logging, HTTP+SSE transport).
- Docs are in English; `docs/research/` holds research snapshots as of 2026-08-08.

## Needs deep dive

- **Codegen strategy.** Kiota vs NSwag vs OpenAPI Generator vs hand-written client.
  The spec is OpenAPI **3.1** — several .NET generators still handle 3.1 poorly.
  Also: 472 schemas with discriminated unions (event types, message parts) — how well
  does each tool map those to C#? System.Text.Json polymorphism vs generated wrappers.
  A spike comparing generators against the real spec is probably the first real task.
- **v1 vs v2 surface.** Both live in one spec (`/session/...` vs `/api/...`, v2
  operation IDs prefixed `v2.`). Upstream's stability commitments are about **v2**.
  Do we ship v2-only, or both? Leaning v2-only unless something essential is missing.
- **Typed event model.** The SSE streams carry a large union of event types. Design the
  .NET representation (polymorphic deserialization, unknown-event forward compat).
- **Server lifecycle helper.** Parity with `createOpencodeServer()`: spawn
  `opencode serve --hostname --port`, scrape stdout for the listening URL, kill on
  dispose. Windows process-tree kill semantics need care.
- **`x-opencode-directory` header** — per-request project targeting. First-class
  option on every call, or client-level default + override?
- **Spec tracking.** `openapi.json` changes on every upstream push to `dev` (CI
  auto-commits). Strategy: pin a snapshot per SDK release + a diff/regen workflow.
  The `external/opencode` submodule is our pinning mechanism today.
- **TFM matrix.** net8.0+ only, or also netstandard2.0? `System.Net.ServerSentEvents`
  has a downlevel package, so netstandard2.0 is feasible — but is it worth it?
- **`pty.connect` WebSocket endpoints** — upstream's own codegen excludes them from
  the generic HTTP pipeline. In scope or explicitly out (like upstream)?
- **Auth** — HTTP basic via `OPENCODE_SERVER_PASSWORD`. Trivial, but decide the API
  shape (client options vs per-request).
- **Testing strategy.** Contract tests against a live `opencode serve` (upstream has
  `test:httpapi` exercising every endpoint — steal the idea); snapshot tests for
  deserialization against captured payloads.
- **Package identity.** Package ID / root namespace (e.g. `OpenCode.Sdk`?), versioning
  relationship to upstream versions.

## TODO / parking lot

- [ ] Repo skeleton: solution, `Directory.Build.props`, central package management,
      `global.json`, CI workflow (build + test on push).
- [ ] Copy/pin the current `openapi.json` snapshot into the repo (traceable to the
      submodule commit) so builds don't depend on the submodule checkout.
- [ ] Codegen spike: run Kiota / NSwag / OpenAPI Generator against the spec, compare
      output quality on unions, SSE endpoints, and the v1/v2 split. Write up results
      in `docs/research/`.
- [ ] Decide package naming + license (upstream opencode is MIT).
- [ ] Later: MCP server project skeleton on ModelContextProtocol.AspNetCore
      (streamable HTTP) + stdio.
- [ ] Later: figure out what an "opencode HQ" / dashboard consumer would need from the
      SDK (multi-instance aggregation lives above the SDK, not in it).
