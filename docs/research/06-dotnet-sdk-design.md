# .NET SDK design decisions: packaging, process management, TFMs

Date: 2026-08-08

> Dated evidence and decision history, not current policy. Follow current canon through
> `AGENTS.md`; several construction and generation positions below were superseded later.
>
> Research snapshot, 2026-08-08 (second session, same day). Records the design
> discussion that shaped the package structure, the process-launcher strategy, and
> the TFM/polyfill matrix. Decisions were reflected in the then-current `GOAL.md`, which was
> later dissolved into the repository's canonical and operational documents.

## 1. Construction strategy: hybrid (hand-written core + mechanical model layer)

Goal explicitly stated by the project owner: a hand-crafted, "professional pride" SDK —
not a generator dump. The constraint pulling the other way: the spec has **472 schemas
/ 162 paths and upstream regenerates `openapi.json` on every push to `dev`**. A
hand-maintained model layer would drift within weeks (the abandoned Stainless SDKs died
of exactly this).

Resolution — layered hybrid:

**Hand-written (the SDK's identity):** transport pipeline (auth,
`x-opencode-directory`, idempotency-aware retry), SSE engine (typed
`IAsyncEnumerable<T>`, no auto-reconnect, `after`-cursor resume), process lifecycle
(see §3), DI integration, error model (tagged domain errors + one infra exception,
mirroring upstream's design), pagination, the entire public API surface.

**Mechanically derived (never hand-maintained):** DTOs/models +
`JsonSerializerContext` registration. Preferred mechanism: **our own small generator**
reading `openapi.json` and emitting C# in our style, analyzer-compliant, with the
`[JsonSerializable]` list for free (upstream itself does this — their
`httpapi-codegen` is hand-crafted tooling). Kiota/NSwag/OpenAPI Generator serve as
benchmarks in the spike, not as the presumed answer.

The spike question is therefore reframed: not *"which generator"* but *"which
mechanism keeps the model layer mechanical"* — evaluated on OpenAPI 3.1 support,
discriminated-union mapping, `JsonSerializerContext` emission, auto-generated marking
(the skeleton's `AnalysisMode=All` + `TreatWarningsAsErrors` will otherwise reject the
output), and v2-only filtering.

Also decided: **v2 surface only** (`/api/*` routes, `v2.`-prefixed operation IDs) —
upstream's stability commitments are phrased about v2, and the v2 release is imminent.

## 2. DI packaging: precedent research

Question: does DI integration belong in the core package or a companion package?
Method: pulled the latest stable nuspec of 13 comparable SDKs from nuget.org
(2026-08-08) and inspected `Microsoft.Extensions.*` (ME) dependencies.

| Core package | ME deps in core | DI integration |
|---|---|---|
| `OpenAI` 2.12.0 | none (System.ClientModel, **System.Net.ServerSentEvents**) | external ecosystem |
| `AWSSDK.Core` 4.0.100 | none | separate: `AWSSDK.Extensions.NETCore.Setup` |
| `Azure.Core` 1.61 | Config/Hosting.Abstractions (historical) | separate: `Microsoft.Extensions.Azure` |
| `Npgsql` 10.0.3 | Logging.Abstractions only | separate: `Npgsql.DependencyInjection` |
| `Polly.Core` 8.7 | none | separate: `Polly.Extensions` |
| `Grpc.Net.Client` 2.83 | Logging.Abstractions only | separate: `Grpc.Net.ClientFactory` (ME.Http) |
| `Refit` 15.0 | none (also ships **System.Net.ServerSentEvents**) | separate: `Refit.HttpClientFactory` (ME.Http) |
| `StackExchange.Redis` 3.1 | Logging.Abstractions only | none |
| `Octokit` 14.0 / `Elastic.Clients.Elasticsearch` 9.5 | none | none |
| `ModelContextProtocol.Core` 2.1 | Logging.Abstractions (+ AI.Abstractions) | higher packages: `ModelContextProtocol` (hosting), `.AspNetCore` |

Consensus is unambiguous: **no DI dependencies in core; DI/IHttpClientFactory wiring in
a companion package.** The one tolerated core dependency is
`Microsoft.Extensions.Logging.Abstractions` (Npgsql, Redis, gRPC, MCP all carry it).
Side finding: OpenAI, Refit, and MCP.Core all depend on `System.Net.ServerSentEvents` —
our SSE plan is standard industry practice.

**Decision:**

```
OpenCode.Sdk              core: STJ, System.Net.ServerSentEvents,
                          ME.Logging.Abstractions, downlevel polyfills;
                          includes the server launcher (§3);
                          HttpClient/handler injectable via constructor
OpenCode.Sdk.Extensions   ME.Http + DI.Abstractions + Options;
                          AddOpenCodeClient(), IHttpClientFactory, options binding;
                          extension methods in the Microsoft.Extensions.DependencyInjection namespace
```

Name follows the `Polly.Extensions` style (owner's preference over
`*.DependencyInjection`). NuGet ID `OpenCode.Sdk` verified available (2026-08-08;
unrelated old `opencode` package exists). Solution file: `OpenCode.slnx`. Future
possibility: `OpenCode.Aspire.Hosting` as a further companion.

## 3. Process launcher: in core, hand-rolled on System.Diagnostics.Process

Initial proposal was a separate CliWrap-based launcher package; **rejected in
discussion** on parity grounds: upstream ships `createOpencodeServer()` inside
`@opencode-ai/sdk` itself, and the MCP C# SDK spawns processes inside
`ModelContextProtocol.Core` (`StdioClientTransport` — **use its implementation as a
reference**). One-package DX ("connect or auto-start") wins.

Key inputs to the decision:

- **.NET 11 (GA 2026-11-10) overhauls the Process API**: `Process.Run[Async]`,
  `RunAndCaptureText`, `ReadAllLinesAsync` (deadlock-free multiplexed line streaming),
  `StartAndForget`, `ProcessExitStatus`, `ProcessStartInfo.KillOnParentExit` (Job
  Objects on Windows, `PR_SET_PDEATHSIG` on Linux), `StartDetached`,
  `SafeHandle.Signal(PosixSignal.SIGTERM)`, plus a Unix rewrite (`posix_spawn`, up to
  100x faster spawn on Apple Silicon). **All net11-only, no backport** — so it can't be
  our baseline (matrix: net472/net8/9/10), but it is the planned light-up path.
- **CliWrap** (3.10.4, actively maintained, netstandard2.0) would cover today's needs,
  but our lifecycle is narrow — one known binary, known args — and doesn't justify a
  third-party dependency in core. CliWrap and dotnet/runtime are both MIT, so adapting
  specific techniques (with attribution) is fine.
- **Upstream parity bar is low**: the JS SDK's `stop()` is `taskkill /pid X /T /F` on
  Windows (zero grace) and plain `SIGTERM` elsewhere (`packages/sdk/js/src/process.ts`).

**Launcher anatomy (hand-rolled, zero extra deps):**

1. Spawn `opencode serve --hostname --port` (+ `OPENCODE_CONFIG_CONTENT` env var);
   async stdout read until `"opencode server listening on <url>"`, with timeout.
   `ArgumentList` on net8+; manual (simple) quoting on net472.
2. After startup, keep draining stdout/stderr in background tasks — a full pipe buffer
   blocks the child (the classic deadlock the .NET 11 API was built to fix).
3. Graceful stop: Unix (net8+) `kill(pid, SIGTERM)` via trivial P/Invoke; Windows —
   skip grace (real Ctrl+C needs the `AttachConsole` dance; upstream doesn't bother
   either).
4. Forceful fallback: `Process.Kill(entireProcessTree: true)` on net8+;
   `taskkill /pid X /T /F` on net472 (identical to upstream's fallback).
5. Orphan protection (bonus): Windows Job Object with `KILL_ON_JOB_CLOSE` (~50 lines
   of P/Invoke; this is what .NET 11's `KillOnParentExit` does internally). Linux
   equivalent waits for the net11 TFM.
6. Later: add `net11.0` TFM; steps 1–5 largely collapse into BCL calls
   (`ReadAllLinesAsync`, `KillOnParentExit`, `SafeHandle.Signal`).

**Acceptance criterion:** the launcher does not merge without a three-OS CI matrix
(Windows/Linux/macOS) running real `opencode serve` start/stop tests.

## 4. TFM matrix, polyfills, ConfigureAwait

**Initial matrix: `net472; net8.0; net9.0; net10.0`.** Correction recorded from the
session: .NET STS support was extended to 24 months — **.NET 9 is supported until
2026-11-10** (same date as .NET 8 LTS), contrary to the assistant's earlier 18-month
assumption. `net472` gives .NET Framework reach and supersedes the earlier
netstandard2.0 question.

Downlevel enablers:

- `Microsoft.Bcl.AsyncInterfaces` → `IAsyncEnumerable<T>` on net472
- `System.Net.ServerSentEvents` package → SSE parser down to netstandard2.0
- Latest `System.Text.Json` package on all TFMs → source-gen + modern polymorphism
  (`AllowOutOfOrderMetadataProperties`) downlevel
- `PolySharp` → compile-time language polyfills (`required`, `init`, nullable attrs)
- net472 transport gotcha: `ServicePointManager.DefaultConnectionLimit` defaults to
  **2** — must be raised for concurrent SSE + request traffic

**ConfigureAwait(false) is mandatory** across the SDK: with net472 in the matrix,
SynchronizationContext deadlocks (WinForms/WPF/classic ASP.NET) are real. Irony noted:
the imported `.editorconfig` disables both enforcing rules (`VSTHRD111 = none`,
`MA0004 = none`, commented "conflicts with modern guidance") — flipping one to `error`
for `src` is the top item of the parked editorconfig review.

**Native AOT:** source-gen STJ (`JsonSerializerContext`) everywhere; the
`[JsonSerializable]` type list is emitted by the model generator; mark
`IsAotCompatible=true` on the net10 (later net11) targets.

## 5. Skeleton review outcome (imported from the LocalStack repo)

Full findings were delivered in-session; actionable items moved from the former `GOAL.md` into the
later canonical and operational work queues. Highlights:
LocalStack identity leftovers (editorconfig header, Authors/Company/URLs/Copyright vs
LICENSE mismatch), pack references to files that don't exist yet (README.md, icon
asset), `OpenTelemetry.Instrumentation.AWS(+Lambda)` to remove, several packages
behind latest (BannedApiAnalyzers and VSTHRD a major behind; NSubstitute 6, TUnit
1.63), no-op `NoWarn`/`NoError` lines. Kept deliberately: Aspire + core OTel packages
(planned local dev/test AppHost with a mini UI driving `opencode serve`),
`BuildOs`/`BuildArch` block (will serve platform-specific opencode binary downloads
for integration tests; values may need adapting to opencode's release asset naming).

## Sources

- [Process API Improvements in .NET 11 — .NET Blog](https://devblogs.microsoft.com/dotnet/process-api-improvements-in-dotnet-11/)
- [CliWrap — GitHub (Tyrrrz)](https://github.com/Tyrrrz/CliWrap) / [NuGet 3.10.4](https://www.nuget.org/packages/CliWrap)
- [.NET support dates (official table provided in session: .NET 8 & 9 → 2026-11-10, .NET 10 → 2028-11-14)](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- MCP C# SDK `StdioClientTransport` (in `ModelContextProtocol.Core`) — reference
  implementation for in-core process spawning
- nuget.org flat-container/nuspec API — dependency graphs in §2 (retrieved 2026-08-08)
