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

## Decisions

- **Target artifact:** `external/opencode/packages/sdk/openapi.json` — the committed,
  CI-regenerated OpenAPI 3.1 spec (162 paths, 472 schemas at opencode 1.18.15). Same
  spec the official JS SDK is generated from.
- **v2 surface only** (`/api/*` routes, `v2.` operation IDs) — upstream's stability
  commitments are phrased about v2, and the v2 release is imminent.
- **Roadmap order: SDK first, MCP server second.** The MCP server becomes a thin
  adapter over our own SDK. (Avoids the unofficial `opencode-mcp`'s private-internals
  trap.)
- **Hybrid construction** (`docs/research/06-dotnet-sdk-design.md` §1): hand-written
  core — transport pipeline, SSE engine, process lifecycle, DI, error model, public
  API — plus a **mechanically derived model layer** (472 schemas are never
  hand-maintained; preferred mechanism is our own generator, Kiota/NSwag serve as
  spike benchmarks).
- **TFM matrix (initial): `net472; net8.0; net9.0; net10.0`.** net8/net9 both
  supported through 2026-11-10 (STS extended to 24 months); net472 gives .NET
  Framework reach (supersedes the earlier netstandard2.0 question). `net11.0` light-up
  planned post-GA (2026-11-10) — mainly for the new Process APIs.
- **Packages:**
  - `OpenCode.Sdk` — core. Deps: System.Text.Json, System.Net.ServerSentEvents,
    ME.Logging.Abstractions + downlevel polyfills. **Includes the server launcher**,
    hand-rolled on `System.Diagnostics.Process`, no CliWrap (upstream parity: JS SDK
    ships `createOpencodeServer()` in-package; MCP C# SDK spawns in Core —
    `StdioClientTransport` is the reference implementation). HttpClient injectable via
    constructor.
  - `OpenCode.Sdk.Extensions` — ME.Http + DI.Abstractions + Options;
    `AddOpenCodeClient()`, IHttpClientFactory wiring, options binding.
  - Future candidates: `OpenCode.Aspire.Hosting`.
  - NuGet ID `OpenCode.Sdk` verified available (2026-08-08). Solution: `OpenCode.slnx`.
    README carries an explicit "unofficial" note.
- **SSE as `IAsyncEnumerable<T>`**, no automatic reconnect (matches upstream design;
  durable per-session stream resumes via `after` cursor).
- **`ConfigureAwait(false)` mandatory** everywhere (net472 in matrix ⇒
  SynchronizationContext deadlocks are real). Analyzer enforcement currently off in
  the skeleton — see Parked.
- **Native AOT friendly:** source-gen STJ (`JsonSerializerContext`; the
  `[JsonSerializable]` list is generator-emitted), `IsAotCompatible=true` on net10+.
- **Aspire stays** — planned local dev/test AppHost (mini UI, `opencode serve` as a
  resource); core OTel packages support that host. AWS instrumentation packages go.
- **Testing:** TUnit; unit + integration/functional tests against a real opencode
  process (details later). Launcher acceptance criterion: three-OS CI matrix
  (Windows/Linux/macOS) with real `opencode serve` start/stop tests.
- **MCP server targets the 2026-07-28 spec** via MCP C# SDK v2.0 (stdio + streamable
  HTTP). No investment in deprecated features (Sampling/Roots/Logging, HTTP+SSE).
- Docs in English; `docs/research/` holds dated research snapshots.

## Needs deep dive

- **Codegen spike (reframed).** Own generator vs Kiota/NSwag/OpenAPI Generator as the
  model-layer mechanism. Evaluate on: OpenAPI 3.1 support, discriminated-union → C#
  mapping, `JsonSerializerContext` emission, analyzer-compliance / auto-generated
  marking, v2-only filtering.
- **net472 spike items:** SSE behavior on long-lived responses
  (`ServicePointManager.DefaultConnectionLimit = 2` gotcha), async stdout reading,
  `taskkill /T /F` tree-kill fallback, polyfill set validation
  (`Microsoft.Bcl.AsyncInterfaces`, `PolySharp`, latest STJ package downlevel).
- **Typed event model.** SSE payloads are a large discriminated union — design the
  .NET representation (`[JsonPolymorphic]`, `AllowOutOfOrderMetadataProperties`,
  unknown-event forward compat).
- **`x-opencode-directory` header** — per-request project targeting: first-class
  option on every call vs client-level default + override.
- **Spec tracking.** `openapi.json` changes on every upstream push; pin a snapshot per
  SDK release + diff/regen workflow (submodule is today's pinning mechanism).
- **`pty.connect` WebSocket endpoints** — upstream's own codegen excludes them;
  probably out of scope for us too.
- **Auth shape** — HTTP basic (`OPENCODE_SERVER_PASSWORD`): client options vs
  per-request.
- **Versioning/release strategy** — `VersionPrefix`, RELEASE_NOTES flow, relationship
  to upstream versions.
- **Testing strategy details** — integration/functional test design against real
  opencode; steal upstream's "every endpoint must be exercised" idea (`test:httpapi`).

## Parked: .editorconfig / analyzer review

- `VSTHRD111` and `MA0004` (ConfigureAwait enforcement) are both `none` — flip one to
  `error` for `src` (tests exempt). Top priority given net472.
- `CA1801` set to `error` under a "Deprecated Rules" heading — rule itself is
  deprecated (superseded by IDE0060); tidy up.
- Generated-code handling: generator output must carry auto-generated headers; add a
  `generated_code = true` editorconfig section for gen folders — otherwise
  `AnalysisMode=All` + `TreatWarningsAsErrors` rejects the model layer.
- Full overlap audit later (CA vs MA vs Sonar duplicate rules; the file already
  documents known overlap groups).

## TODO / parking lot

- [ ] **Skeleton cleanup (single pass):** fix `.editorconfig` header
      (LocalStack.Aspire.Hosting → this repo); replace LocalStack identity in
      `Directory.Build.props` (Authors/Company/Owners/URLs); align `Copyright` with
      LICENSE (Deniz İrgin); drop `AspireAppHostSdkVersion`; drop no-op
      `NoWarn`/`NoError` lines; remove `OpenTelemetry.Instrumentation.AWS` +
      `AWSLambda`; keep `BuildOs`/`BuildArch` (adapt values to opencode release-asset
      naming when the binary-download need lands); consider
      `PackageLicenseExpression=MIT`; add README.md + icon asset before first pack
      (icon name TBD).
- [ ] **Package bumps** (as of 2026-08-08): Meziantou 3.0.108→3.0.140,
      BannedApiAnalyzers 4.14→5.6, VSTHRD 17.14→18.7, NSubstitute 5.3→6.0,
      TUnit 1.56→1.63, Sonar 10.27→10.31, NetAnalyzers/SourceLink patch bumps.
- [ ] Repo skeleton: `OpenCode.slnx`, `src/`+`tests/` layout, CI workflow (build+test;
      three-OS matrix when the launcher lands).
- [ ] Pin the current `openapi.json` snapshot into the repo (traceable to the
      submodule commit).
- [ ] Codegen spike (scope above) — write results into `docs/research/`.
- [ ] Later: MCP server project on ModelContextProtocol.AspNetCore + stdio.
- [ ] Later: "opencode HQ" consumer needs — multi-instance aggregation lives above
      the SDK, not in it.
