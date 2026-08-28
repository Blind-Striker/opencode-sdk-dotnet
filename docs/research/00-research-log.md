# Research log — 2026-08-08

Date: 2026-08-27

> Dated evidence and decision history, not current policy. Follow current canon through
> `AGENTS.md`; later sessions in this log intentionally supersede some earlier conclusions.
>
> How this project's understanding was built: the questions asked, how each was
> researched, what was found, and what decision or lesson came out of it.
> Chronological. Details live in the numbered topic docs; this is the chain.

Reference snapshots used by the initial sessions (git submodules; later sessions record pin
changes explicitly):

- `external/opencode` — the opencode monorepo (SDK at version 1.18.15)
- `external/opencode-mcp` — unofficial MCP server for opencode (npm `opencode-mcp@1.11.0`)

---

## Q1: How mature is the unofficial `opencode-mcp` server?

**How researched:** full-repo exploration — README/docs vs source cross-check, tool/
resource/prompt counts verified against code, test suite inspection, git history and
npm publish history, open issues/PRs on GitHub.

**Found:** genuinely well-built for a solo project (docs match implementation exactly;
~1:1 test-to-source ratio; real security hygiene) but with disqualifying process gaps:
zero CI, the entire HTTP transport test suite disabled (`describe.skip`), a dependency
on the JS SDK's **private** `_client` field behind a floating version range, a retry
loop that replays non-idempotent requests, and ~2.5 months of maintainer silence with
open bug-fix PRs. Verdict: *early but functional, not production-ready.*
→ Details: [03-opencode-mcp-assessment.md](03-opencode-mcp-assessment.md)

**Decision/lesson:** the architecture (MCP adapter over an SDK) is right; the execution
shows exactly what to avoid. **Own your HTTP layer** — that's what an SDK *is*. Never
depend on another SDK's private internals. CI from day one.

## Q2: What exactly is "the opencode SDK"?

**How researched:** explored `packages/sdk`, `packages/protocol`, the server route
definitions, the generation scripts, and the CI workflows in the opencode monorepo.

**Found:** opencode is a **client/server product** — the TUI is just a client of a local
HTTP server. The SDK (`@opencode-ai/sdk`) is a generated, typed HTTP client for that
server. Pipeline: Effect `HttpApi` definitions → committed OpenAPI 3.1 spec
(`packages/sdk/openapi.json`) → `@hey-api/openapi-ts` → thin hand-written wrapper.
The TUI, web UI, desktop app, plugins, and Slack integration all consume the same SDK.
→ Details: [01-opencode-architecture.md](01-opencode-architecture.md)

**Decision:** our .NET SDK targets `packages/sdk/openapi.json` — the same artifact the
official JS SDK is generated from. Feature parity with the TUI comes for free.

## Q3 (follow-up): Does the SDK *also* start the opencode process? What is sdk-next? Is the MCP server just "SDK + MCP on top"?

**How researched:** read `packages/sdk/js/src/server.ts` and
`opencode-mcp/src/server-manager.ts` directly (also correcting an earlier
mischaracterization: `createOpencodeServer()` is **not** in-process — it spawns
`opencode serve` as a child process via cross-spawn and scrapes stdout for the URL).

**Found:** the SDK's core job is the typed HTTP client; process launching is a separable
convenience helper. The MCP server uses the SDK for server lifecycle only (auto-start
as a fallback after a health probe), and hand-dispatches actual HTTP through the SDK's
private internals. sdk-next is a different thing entirely — see Q4.

**Decision:** our SDK should offer the same separable split: pure client + optional
`opencode serve` launcher helper.

## Q4: Is the HTTP surface going away with sdk-next / opencode v2? What does "in-process/embedded" mean?

**How researched:** read `packages/sdk-next/README.md` and the design notes in
`CONTEXT.md` (the "Client contract architecture" section and surrounding decisions).

**Found:** **no — HTTP becomes *more* central, not less.** The `HttpApi` contract is
authoritative; networked and embedded modes share the same client and differ only in
transport. Embedded mode (think ASP.NET Core `TestServer`) runs the server's router
in-memory for same-process JS consumers only. Upstream explicitly commits to preserving
v2 route paths, operation IDs, codecs, errors, and OpenAPI output through the
transition. Also captured upstream's streaming design decisions (no auto-reconnect;
live vs durable event streams are distinct APIs).
→ Details: [02-sdk-next-and-http-stability.md](02-sdk-next-and-http-stability.md)

**Decision:** the .NET SDK's target surface is stable by upstream's own commitments.
Embedded mode is unreachable from .NET by definition and is not a competitor to this
project. Mirror upstream's no-auto-reconnect stance for SSE.

## Q5: Are there official SDKs for other languages?

**How researched:** GitHub search across orgs, README + commit-history inspection of
`anomalyco/opencode-sdk-{python,go,js}`.

**Found:** nominally yes, practically no. Stainless-generated Python (frozen at
0.1.0-alpha.36 since 2025-08-27) and Go (frozen at v0.19.2 since 2025-12-18) SDKs exist
with bot-only commit history; the separate JS one was superseded by the in-repo SDK.
The Go SDK died with the old Go TUI (TUI is now TypeScript/SolidJS). Community SDKs
(Rust, Elixir, PHP, Python) exist but none has real traction. **No .NET SDK exists.**
→ Details: [04-ecosystem.md](04-ecosystem.md)

**Decision:** the gap is real; this project fills it.

## Q6: SSE — what is it, does .NET support it? Can a separate UI / "opencode HQ" be built on the SDK?

**Found:** SSE = one-way server→client event stream over a single long-lived HTTP
response (`text/event-stream`). .NET support is first-class since .NET 9
(`System.Net.ServerSentEvents` / `SseParser`, downlevel package available).
A custom UI is exactly what the architecture is for — every existing front-end is an
API client. Caveat for fleet/HQ scenarios: the API is bound to a single instance;
cross-instance aggregation must be built above the SDK (upstream explicitly does not
expose server-global event aggregation).
→ Details: [05-mcp-v2-and-dotnet.md](05-mcp-v2-and-dotnet.md) (SSE section)

## Q7: MCP 2026-07-28 + MCP C# SDK v2.0 — is "SDK first, then MCP server" the right roadmap?

**How researched:** fetched the official spec changelog
(modelcontextprotocol.io/specification/2026-07-28) and the .NET blog announcement of
the C# SDK v2.0 (both post-date the assistant's knowledge cutoff — verified live).

**Found:** the 2026-07-28 revision is the largest since launch — protocol is now
stateless (no `initialize` handshake, no `Mcp-Session-Id`), MRTR replaces
server-initiated requests, `subscriptions/listen` replaces the GET stream, tasks moved
to an extension, and Sampling/Roots/Logging are deprecated. C# SDK v2.0 implements it
(`ModelContextProtocol.Core` / `ModelContextProtocol` / `.AspNetCore`; net8.0–net10.0 +
netstandard2.0). Stateless MCP fits opencode's per-request `x-opencode-directory`
model naturally.
→ Details: [05-mcp-v2-and-dotnet.md](05-mcp-v2-and-dotnet.md)

**Decision:** roadmap confirmed — SDK first, MCP server second as a thin adapter,
targeting the 2026-07-28 spec, offering both stdio and streamable HTTP (the unofficial
server is stdio-only).

## Q8: What is ACP, and how does it differ from MCP?

**Found:** ACP = **Agent Client Protocol** (originated at Zed): the protocol between an
*editor* (client) and a *coding agent* (server) — "LSP for agents". Direction is the
key difference: in MCP the agent consumes tools; in ACP the agent *provides* service to
an editor. opencode implements the agent side (`@agentclientprotocol/sdk` dependency,
`opencode acp` command, `src/acp/service.ts`). ACP is a third integration surface, not
an alternative to the HTTP API our SDK targets.
→ Details: [04-ecosystem.md](04-ecosystem.md) (protocol map section)

---

# Session 2 — 2026-08-08 (evening): .NET skeleton and SDK design

A .NET skeleton (editorconfig, Directory.Build/Packages.props, global.json, etc.) was
imported from the owner's LocalStack Aspire repo. This session reviewed it and settled
the SDK's construction strategy, packaging, TFMs, and process management.
→ Details: [06-dotnet-sdk-design.md](06-dotnet-sdk-design.md)

## Q9: Is the imported skeleton sound? Anything outdated or inconsistent?

**How researched:** read all 8 files; verified package currency against nuget.org;
checked the installed SDK against `global.json`.

**Found:** high-quality, coherent base (editorconfig ↔ analyzer set ↔ CPM aligned) with
LocalStack identity leftovers, pack references to nonexistent files, a few
major-version-behind packages, and no-op property lines. One assistant error corrected
by the owner: **.NET STS support was extended to 24 months — .NET 9 is supported to
2026-11-10**, so the "net9 already EOL" claim was wrong.

**Decisions:** TFM matrix `net472;net8.0;net9.0;net10.0`. Keep Aspire + core OTel
(planned local dev/test AppHost + mini UI). Keep `BuildOs`/`BuildArch` (future opencode
binary downloads for integration tests). Remove AWS OTel instrumentation. Cleanup list
→ The then-current `GOAL.md` TODO, later dissolved into `AGENTS.md` and `docs/ROADMAP.md`.
Parked: full editorconfig/analyzer contradiction review.

## Q10: Fully generated client, or hand-crafted?

**Found/decided:** neither — **hybrid**. Hand-written core (transport, SSE engine,
process lifecycle, DI, error model, public API) + mechanically derived model layer
(472 schemas can't be hand-maintained against a spec regenerated on every upstream
push). Preferred mechanism: our own generator (upstream's own `httpapi-codegen` is the
precedent); Kiota/NSwag as spike benchmarks. Also decided: **v2-only surface**, Native
AOT via source-gen STJ, `ConfigureAwait(false)` mandatory (net472 in matrix — and the
skeleton currently disables both enforcing analyzer rules; parked item #1).

## Q11: DI integration — core package or companion? What do precedent SDKs do?

**How researched:** nuspec dependency graphs of 13 comparable SDKs (OpenAI, AWS,
Azure, Npgsql, Polly, Grpc, Refit, Redis, Octokit, Elastic, MCP) via nuget.org API.

**Found:** unambiguous industry consensus — no DI deps in core (at most
`ME.Logging.Abstractions`); DI/IHttpClientFactory wiring in a companion package.
Bonus: OpenAI, Refit, and MCP.Core all ship `System.Net.ServerSentEvents` — validates
our SSE approach.

**Decision:** `OpenCode.Sdk` (core) + `OpenCode.Sdk.Extensions` (name per owner
preference, Polly style). NuGet ID availability verified. Solution: `OpenCode.slnx`.

## Q12: Process management — CliWrap, raw Process, or the new .NET APIs?

**How researched:** web-verified the .NET 11 Process API overhaul (post-cutoff) and
CliWrap's maintenance state; read the JS SDK's `process.ts` for the parity bar.

**Found:** .NET 11 (GA 2026-11-10) ships exactly what we need (`ReadAllLinesAsync`,
`KillOnParentExit`, `SafeHandle.Signal`) but **net11-only**. CliWrap is alive (3.10.4)
but our lifecycle is one known binary with known args. Upstream's own `stop()` is
crude (`taskkill /T /F`, no grace).

**Decision (owner overruled the separate-package proposal, and was right):** launcher
lives **in core**, hand-rolled on `System.Diagnostics.Process`, zero extra deps —
upstream parity (`createOpencodeServer()` is inside `@opencode-ai/sdk`) + MCP C# SDK
precedent (`StdioClientTransport` spawns inside `ModelContextProtocol.Core`; use as
reference implementation). Six-point anatomy + net11 light-up plan in doc 06.
Acceptance criterion: three-OS CI matrix with real `opencode serve` start/stop tests.

---

# Session 3 — 2026-08-08 (night): analyzer & .editorconfig policy

Trigger: the parked analyzer items in the then-current `GOAL.md` plus a ChatGPT conversation the owner
had reviewed the (ancestor of the) skeleton with a few weeks earlier — shared with
the explicit instruction to treat it as unverified input.
→ Details: [07-analyzer-policy.md](07-analyzer-policy.md)

## Q13: Are the ChatGPT conversation's claims sound?

**How researched:** background agent verifying 13 extracted claims against primary
sources only — Microsoft Learn, dotnet/sdk + dotnet/roslyn sources, analyzer repos'
own docs, extracted nupkgs, and grep over the locally installed SDKs 8/9/10.

**Found:** 8 confirmed, 3 nuanced, plus corrections. Notable: the "NetAnalyzers
package + EnableNETAnalyzers conflicts/warns" story is stale — the warning existed
only in SDK 8 and the 10.x package ships no MSBuild logic at all, so **nothing warns
anymore when the pinned package falls behind the SDK** (package hygiene is entirely
on our bump routine). `AnalysisLevel=latest` resolves to a hardcoded `10.0` for the
whole SDK 10 line, defusing most of the "moving target" concern.
`CodeAnalysisTreatWarningsAsErrors` exempts exactly the CA ID list — third-party
analyzers are untouched by it. The chat's overlap table had one factual error
(CA1849's Meziantou twin is MA0042, not MA0045) and one wrong framing (the
dead-code trio is complementary, not redundant). Local audit bonus: the skeleton's
`.editorconfig` had already absorbed part of that conversation (CA1812 → suggestion,
comment verbatim), and its "Deprecated Rules" section configured two rules that
don't even ship (CA1801 removed in v6, CA1500 never ported from FxCop).

**Decision/lesson:** the grain-of-salt instinct was right — most claims held, but
the ones that didn't (stale doc folklore, wrong rule IDs) are exactly the kind that
propagate silently. Verify against artifacts, not docs alone.

## Q14: What do prominent OSS .NET libraries actually do?

**How researched:** community survey of 11 actively maintained repos (skewed toward
NuGet libraries multi-targeting old TFMs), reading their committed build files.

**Found:** ConfigureAwait enforcement, where present, is uniformly **CA2007 on
product code / none on tests** (dotnet/runtime, Polly, OTel); only the Meziantou
ecosystem uses MA0004 — *instead of* CA2007, with a comment naming the winner.
**0 of 11 repos** use `CodeAnalysisTreatWarningsAsErrors`; none uses
`AnalysisMode=All`; analyzer-heavy repos gate TreatWarningsAsErrors on CI/Release to
protect human inner loops (Meziantou.NET.Sdk even *enables* it when it detects an
LLM agent). Overlaps are resolved explicitly per rule with winner-naming comments;
"both on" survives only where rules genuinely differ. Polly is the closest
structural precedent (Sonar + BannedApi + old TFMs, per-ProjectType globalconfigs).

## Q15: Converge to the community, or keep the maximalist posture?

**Found/decided (owner decision after discussion):** keep `AnalysisMode=All` +
unconditional TWAE — fail-closed (new rules break the build and force a recorded
decision) fits a greenfield, agent-driven repo; the community's softening exists to
protect human dev loops we don't have yet. Converge instead on determinism and
explicitness: `LangVersion` and `AnalysisLevel` pinned numerically, every overlap
pair explicit on both sides, Sonar's implicit strictness documented as chosen
policy, zombie rules deleted. ConfigureAwait becomes triple-enforced
(CA2007 + MA0004/Always + VSTHRD111 — redundancy is deliberate; single fix satisfies
all three), tests exempt. Full decision table (D1–D9): doc 07 Part IV.
One owner-supplied fact folded in: `GenerateDocumentationFile=true` must stay —
IDE0005 doesn't fire in CLI builds without it (guard comment added to props).

---

## Standing conclusions after Session 3 (historical)

These were the working conclusions at this point in the chronology. Later sessions below revise
several of them; current policy lives in the routed canon, not this checkpoint list.

1. Target `packages/sdk/openapi.json`; **v2 surface only** — it carries upstream's
   stability guarantees.
2. SDK first, MCP server second. The MCP server must be a thin consumer of our SDK.
3. SSE → `IAsyncEnumerable<T>`, no auto-reconnect, cancellation via token.
4. Treat `opencode-mcp` as a cautionary reference implementation, not a foundation.
5. Embedded/sdk-next does not threaten this project; it strengthens the HTTP contract.
6. Hybrid construction: hand-written core, mechanically derived models — models are
   never hand-maintained.
7. Two packages: `OpenCode.Sdk` (core, launcher included) + `OpenCode.Sdk.Extensions`
   (DI). TFMs: netstandard2.0 + net472 + net8/9/10, net11 light-up later.
8. Analyzer policy is fail-closed maximalist (`AnalysisMode=All`, unconditional
   TreatWarningsAsErrors) with pinned versions and explicit per-rule arbitration;
   ConfigureAwait is triple-enforced in product code, exempt in tests. Rationale
   and decision table: doc 07.

# Session 4 — 2026-08-08 (day 2): docs infrastructure, structural skeleton, TFM validation

(Earlier the same day, a non-research session dissolved GOAL.md into AGENTS.md +
docs/ROADMAP.md and scaffolded docs/agents/.)

## Q16: Does adding net472 force a netstandard2.0 target? What does Microsoft recommend today?

**How researched:** Microsoft Learn primary sources read in full — "Cross-platform
targeting for .NET libraries" (page updated 2026-04) and ".NET Standard overview" —
DO/CONSIDER/AVOID recommendations extracted verbatim.

**Found:** ns2.0 is not mandatory next to net472 — they are alternative bridges to
.NET Framework, and NuGet nearest-match hands a net472 consumer the net472 asset
whenever one exists. The current official recipe: "DO start with `net8.0` or later",
"CONSIDER `netstandard2.0` if you need broad compatibility or .NET Framework
support", "CONSIDER adding `net462` when you're also targeting `netstandard2.0`"
(consuming ns2.0 from old Framework has known issues, fixed in 4.7.2). .NET Standard
is frozen at 2.1 ("no new versions will be released") yet explicitly "not
deprecated"; MS's own BCL packages ship net462 + ns2.0 + modern TFMs.

**Decided (owner proposal, endorsed):** TFM matrix becomes
`netstandard2.0;net472;net8.0;net9.0;net10.0`. net472 stays for Framework-exact
compile paths (`#if NET472`: ServicePointManager, process tree-kill); ns2.0 rides
the same downlevel tax net472 already imposes (PolySharp + polyfills) and reaches
consumers who otherwise could not install the package at all (net5–net7 stragglers,
Unity, Mono). ns2.0 has no runtime of its own — the net472 test leg is its proxy
coverage. Reversal note: this re-adds the ns2.0 target that the original net472
decision had dropped; both facts now hold (exact TFM *and* bridge TFM), matching
MS's own BCL practice.

## Q17: PolySharp or SimonCropp/Polyfill for the downlevel TFMs?

**How researched:** Polyfill README (source-only package docs) against known PolySharp
scope; latest versions checked on nuget.org (Polyfill 11.0.2).

**Found:** the two solve different layers. PolySharp (Sergio Pedri) is an incremental
source generator emitting only the *compiler-support* types modern C# needs downlevel
(IsExternalInit, required-member/nullability attributes, Index/Range, …) — zero BCL
API coverage. Polyfill (Simon Cropp) is a source-only package that ships those same
attributes *plus* hundreds of BCL API polyfills as extension methods
(`Stream.ReadAsync(Memory<byte>)`, `Task.WaitAsync`, cancellation-aware `File.*Async`,
span-based string ops, …), targets net461+/netstandard2.0+, expects a current
LangVersion, and upstream recommends referencing it on **all** TFMs (internal types,
PrivateAssets=all — nothing leaks to consumers).

**Decided (owner proposal, endorsed):** switch to Polyfill before any SDK code exists.
Transport/SSE/launcher code will hit BCL API gaps on ns2.0/net472 immediately —
PolySharp would leave us hand-rolling internal shims for exactly the APIs Polyfill
already maintains. Interplay noted with the LangVersion=14.0 numeric pin: Polyfill
tracks current C#, so future language bumps happen by moving the pin deliberately.

## Q18: CSharpier — wire it, or stay on dotnet format?

**How researched:** discussion from mechanisms (owner invited pushback): what
.editorconfig/IDE0055 actually specify (local toggles; line wrapping is left
unspecified — two conflicting layouts both pass), Roslyn's formatter behavior
(normalizes around existing structure, never re-wraps) vs CSharpier's
printer architecture (re-prints from the AST; output is a function of code +
line width; reads core .editorconfig keys, ignores `csharp_*` toggles). Costs
on the table: IDE plugin requirement, IDE0055 + Roslynator.Formatting ceding
ceremonies, one more tool in the bump routine.

**Decided (owner):** stay on **dotnet format** — CI gate `dotnet format
--verify-no-changes` (one CI leg), IDE0055 stays `error` in-build, IDE flow
remains pure .editorconfig with no plugin. Wrapping determinism is knowingly
given up. `max_line_length` set to 150 (advisory — guidance for IDE rulers and
code-writing agents; nothing in the C# toolchain enforces it). MA0051 method
limits stay 80 lines / 60 statements. This supersedes the CSharpier clause of
D8 in doc 07 (was: "decided in principle, wire with the first csproj").

## Q19: What OpenAPI dialect is the spec actually written in, and how does upstream itself generate from it?

**How researched:** counted constructs by recursive walk over the parsed
`spec/openapi.json`; read upstream's generation tooling in the submodule
(`packages/sdk/js/script/build.ts`, `packages/httpapi-codegen`,
`packages/client`).

**Found:** the spec is a strict, discriminator-free `anyOf` dialect of
OpenAPI 3.1: 172 `anyOf`, 0 `discriminator`, 0 `allOf`, 0 hard-3.1 constructs
(no type arrays / `const` / `$defs`); unions self-identify via 513
single-value-enum literal markers (`"type": {"enum": ["text"]}`). Upstream's
published JS SDK fights its off-the-shelf generator (pre-gen document surgery,
guarded post-gen regex patches); its next-gen client is generated by a
hand-rolled ~1.2k-line codegen with caller-side endpoint filtering and
committed, CI-regen-verified output. Also: 3 of 61 `v2.*` operations live
outside `/api/*` (under `/experimental/project/{projectID}/copy*`), and `Part`
is referenced only by legacy routes — the v2 event union is `V2Event`.
→ Details: [08-codegen-spike.md](08-codegen-spike.md)

**Decision/lesson:** union mapping (criterion K2) is the decisive codegen
criterion, not OpenAPI 3.1 support; v2 filtering must key on the operationId
prefix, not the path.

## Q20: Can Kiota, NSwag, or OpenAPI Generator produce our model layer?

**How researched:** three parallel benchmark prototypes against the real spec
(Kiota 1.34.1, NSwag 14.7.1/NJsonSchema 11.6.1, OpenAPI Generator 7.24.0
csharp/generichost), each with a strict-analyzer compile harness replicating
the repo regime; preceded by a primary-source capability survey.

**Found:** all three fail the union criterion structurally (intersection
wrappers / empty classes with extension-data bags / 88-way speculative
parsing), and none can emit the locked single-registry `JsonSerializerContext`
(Kiota: no STJ by design; NSwag: no capability; OpenAPI Generator: 893
per-model contexts that don't compile on this spec — SYSLIB1031 ×416). NSwag
additionally misparses 3.1 silently as 3.0 and its output doesn't compile
(three independent layers); OpenAPI Generator reproduced five emission-bug
classes and never produced a compiling model layer. Kiota is the only clean
toolchain but reduces the SSE endpoint to `Stream` (the entire event union
vanishes) and throws on unknown enum values.
→ Details: [08-codegen-spike.md](08-codegen-spike.md)

**Decision:** off-the-shelf generation is eliminated on run evidence — the
model layer comes from **our own generator**.

## Q21: Own generator — Roslyn syntax trees or template/string emission?

**How researched:** implemented the same slice twice over a shared parser/IR
(~190 lines): `Part` transitive closure (3 unions incl. a nested
`status`-keyed one, 35 objects) → 39 files each; compiled both under the
strict harness; functional smoke test of polymorphic dispatch.

**Found:** STJ name-based polymorphism maps 1:1 onto the spec's literal
convention — emitted `[JsonPolymorphic]`/`[JsonDerivedType]` dispatch works on
real payloads (nested unions, out-of-order discriminators; unknown
discriminator throws — forward-compat is an API-design question). Both outputs
pass the full analyzer wall 0/0 via the `.g.cs` generated-code convention
(each file then needs its own `#nullable enable`); the on-merit probe (files
renamed `.cs`) leaves 186 diagnostics, ~91% two mechanical style rules, all
fixable only in an own emitter. The Roslyn emitter added a dependency and ~4×
runtime while still pushing XML docs, directives, and formatting through
strings; the template emitter controls formatting exactly.
→ Details: [08-codegen-spike.md](08-codegen-spike.md)

**Decision (owner):** **Roslyn syntax-tree emission.** The slice evidence priced
the trade (template: no dependency, ~4× faster, exact formatting control;
Roslyn: semantic construction) and the maintainer chose semantic construction
for maintainability at full-generator scale — the generator will emit far more
than flat models. Measured costs are mitigated: `dotnet format` post-step owns
formatting; doc/directive trivia is emitted as parsed strings (standard
practice).

## Q22: Own generator — standalone CLI tool or Roslyn incremental source generator?

**How researched:** specialist analysis (roslyn-incremental-generator
domain) argued from mechanisms: generator pipeline caching against a single
~1 MB input, multi-TFM execution economics, review/determinism, analyzer
interplay, and the downstream STJ source-generator dependency.

**Found:** the incremental shape has a structural blocker, not just cost:
Roslyn generators never see each other's output, so a compile-time-emitted
`[JsonSerializable]` registry is invisible to the STJ source generator — it
silently emits nothing and the AOT commitment degrades to reflection.
Economics point the same way: the spec changes once per upstream release,
while an incremental generator pays parse+emit+analyze on every build × 5 TFMs
× 3 CI OSes × IDE; committed output gives reviewable spec-refresh diffs, which
compile-time generation forfeits.
→ Details: [08-codegen-spike.md](08-codegen-spike.md)

**Decision:** **repo tooling under `tools/`** — emission engine as a library
behind a thin file-based `.cs` entry (committed with the executable bit), bound
to the repo build rules; output committed into the SDK project, CI
regen-verifies, and the same tool owns spec refresh (submodule pin bump,
`spec/` copy, `SNAPSHOT.md` stamp). Reversal triggers recorded in doc 08 for
the eventual ADR.

## Q23: What is "v2" exactly — and does the pinned `v2.*` surface carry forward to opencode 2.0?

**How researched:** local submodule inspection (read-only) + remote `2.0`
branch of `sst/opencode` via raw.githubusercontent.com + GitHub Releases API +
npm dist-tags; triggered by the owner catching a real conceptual ambiguity
(product version vs API surface version).

**Found:** two independent version axes. Product: 1.18.15 stable (`dev` +
tags) vs 2.0 (separate branch, daily timestamped npm betas, no tags yet). API
surface: the 1.18.15 spec carries TWO surfaces (127 legacy ops + 61
transitional `v2.*` ops under `/api`), while the 2.0 spec carries ONE — 112
plain dotted operationIds at root, `v2.` prefix gone, legacy gone. Only 15/61
`v2.*` names survive into 2.0 (systematic renames); the schema dialect carries
(discriminator-free `anyOf`, `Part` shape-identical) but literals move from
single-value `enum` to `const` (138×). No formal deprecation statement; the
evidence is the 2.0 spec itself plus in-code "remove in v2" comments. Crucial
capability fact: the modern block does not yet cover everything — upstream's
own TUI still runs 91 legacy vs 18 v2 call sites.
→ Details: [09-upstream-v1v2.md](09-upstream-v1v2.md)

**Decision (owner):** generate **both surfaces** of the pinned 1.x spec — the
MCP-server goal needs today's full capability and v2-only cannot deliver it.
Deep integration tests target the modern surface; legacy is best-effort.
Public naming strips the `v2.` prefix ("V2" never appears in type/client
names); the 2.0 rename wave is absorbed at a major release, evolve/deprecate
decided on the evidence then. Supersedes the "v2 surface only" lock (Q-era
doc 02/06), which predated the capability-coverage and transitional-surface
findings.

## Q24: Align NuGet versioning with upstream opencode versions?

**How researched:** discussion from mechanisms — owner proposed
`major.minor` sync with owned `patch` (precedent: owner's Aspire repo);
weighed against semver signaling.

**Found:** alignment would force our own features (launcher, DI, non-spec
work) onto patch releases, surrendering semver's "minor = features" signal;
its main benefit — absorbing the opencode-2.0 rename wave at a major — is
available anyway via an explicit breaking major with release notes.

**Decision (owner):** **independent semver**, no upstream alignment. Pre-1.0
numbering, RELEASE_NOTES flow, and the publish workflow remain open (ROADMAP).

---

# Session 5 — 2026-08-08 (day 2, evening): grill session — ADR backfill and canonicalization

Grill session (`grill-with-docs`) against `AGENTS.md` + docs with the spike evidence in hand.
The decisions live in ADRs 0001–0006; this entry is the chain only.

## Q25: Where do the ADR instructions live?

**Found/decided:** `docs/adr/README.md` becomes the canonical home (criteria, format,
numbering, `Date:` rule; no index — the directory listing is the index). `AGENTS.md` keeps
statements + links; `docs/agents/domain.md` stays a consumption guide, stripped of
harness-specific skill names. Layering: `AGENTS.md` statement → ADR decision+why → research
doc evidence. Commit-history mining surfaced one misplaced decision (independent semver sat
in ROADMAP Open Questions) — moved to Locked Decisions + ADR-0006. Rejected as ADRs: test
naming and one-way doc references (conventions, `AGENTS.md`), CSharpier/PolySharp rejections
(research-log history), analyzer-policy pointer (Hard Rule + doc 07 suffice).

## Q26: Does "strip the `v2.` prefix" survive both-surfaces?

**Found:** 16 of 61 modern names collide with legacy names once the prefix is stripped
(counted from the pinned spec: six `session.*`, `provider.list`, `command.list`,
`event.subscribe`, all `pty.*`).
**Decided:** structural separation — the modern surface takes the unmarked names; legacy
lives behind an explicitly legacy-marked sub-surface, deleted wholesale at our 2.0-absorbing
major (ADR-0005).

## Q27: Where does the MCP server live?

**Found/decided (owner):** monorepo — the thin-adapter architecture wants compile-time
coupling (cross-repo private-internals dependency was the unofficial `opencode-mcp`'s failure
mode), the consumer-driven legacy-test scope stays mechanically derivable, and the repo's
infrastructure is paid for once. Purpose statement made explicit. Versioning: every package
independent (owner overruled a lockstep-family proposal); per-merge GitHub Packages CD +
manual NuGet.org release; no monorepo tooling at this scale (ADR-0006). NuGet `McpServer`
package type evaluation queued with the MCP phase. Repo name stays (`opencode-sdk-dotnet` —
the MCP server is the SDK's agent-facing surface).

## Q28: Is "legacy best-effort" honest when the MCP server leans on legacy?

**Decided:** consumer-driven testing — deep integration testing covers the modern surface
plus every legacy operation the MCP server consumes (set derived mechanically from the
in-repo MCP project's SDK calls); the rest stays best-effort (ADR-0005).

## Q29: Generated code — analyzer exemption or on-merit conformance?

**Found:** the spike's on-merit probe left 186 diagnostics, ~91% two mechanical style rules,
all fixable only in an own emitter; the `.g.cs` exemption also suppresses project-level
`<Nullable>` (generated files need explicit `#nullable` opt-in — CS8669).
**Decided (owner):** on-merit — no blanket exemption for the emitted layer; per-rule
arbitration for rules that genuinely cannot apply; accepted cost: the emitter tracks the
analyzer wall permanently. Mechanics settled at build-out (ADR-0003 Consequences).

## Q30: Remaining confirmations

Unknown-discriminator forward compatibility stays parked for the API design session (version
skew between the pinned spec and a newer live server is why it exists at all). The
Roslyn-emission record keeps both sides with the IR-boundary reversal framing (ADR-0003).
Generator/SSE boundary made explicit in ROADMAP: the generator emits `x-effect-stream` item
schemas; stream endpoints are wired by hand through the SSE engine. Root `CONTEXT.md`
created (upstream domain terms + project language: modern/legacy surface, Launcher, Spec
pin, Model layer), aligned with upstream's durable-vs-live stream distinction.

# Session 6 — 2026-08-09: grill session — public API design spec

Grill session (`grill-with-docs`) against the API design spec with full onboarding (docs
00–10, ADRs, the spec) and scripted primary-source verification (pinned spec, JS SDK
submodule, `.editorconfig`). Decisions live in ADRs 0007–0009 and the corrected spec;
this entry is the chain.

## Q31: Do the spec's factual claims survive primary-source verification?

**How researched:** scripted counts over `spec/openapi.json` (operations, 204s, error
schemas, envelopes, unions, content types); greps over the JS SDK submodule
(`throwOnError`, interceptors, `omitEndpoints`, `server.ts`) and `.editorconfig`.

**Found:** the load-bearing counts held (61/127 ops, 16 collisions, 44 error-named
schemas, the 8-variant session-error union, envelope families, cursor-encodes-filters —
the last verified in the server source). Five claims fell: "24 of 61 modern ops return
204" (actual 19), "113 `throwOnError` sites in the TUI" (TUI has 0; ~76 non-generated
sites in app/CLI, some client-level), "CA1062 already error" (was `suggestion`),
history as cursor-paged (actually `after`/`limit` + `{data, hasMore}`), and the paged
cursor as a flat string (actually a bidirectional `{previous?, next?}` struct). Also:
upstream's `omitEndpoints` excludes three ops (`fs.read`, `pty.connect`,
`pty.connectToken`), not one; `v2.fs.read` returns `application/octet-stream` and
legacy `vcs.diff.raw` returns `text/x-diff` — the envelope design had no non-JSON
story; `x-effect-stream` exists only on the durable endpoint; `after`/`limit` are
strings in the OpenAPI projection but `NumberFromString` in the Effect source.

**Decision:** spec corrected throughout. Non-JSON bodies generate via a fail-closed
content-type→payload map (`Stream` on a disposable envelope / `string` for text);
stream detection keys on `text/event-stream`; numeric query params ride a new curation
per-parameter type override.

## Q32: Does the guarded-getter envelope survive record semantics?

**Found:** record-synthesized `ToString`/`PrintMembers` calls public getters — logging
a `NoThrow` error envelope would throw from the guard. `with`/equality operate on
fields (safe); `required` + `[SetsRequiredMembers]` work downlevel via Polyfill.
**Decision:** the generator emits a guarded `PrintMembers` override per envelope;
records stay. Same instructive-guard idea applied to the mock seam: the shared
`Pipeline` accessor throws `InvalidOperationException` (never a bare NRE) for
non-overridden members on mocking-constructor instances.

## Q33: Which ADR candidates seal, and where does "public API is hand-written" go?

**Decided:** ADR-0007 (error model: typed exception spine carrying tagged data;
per-call channel), ADR-0008 (all op methods generated; excluded/hand-wired ops
fingerprint-pinned — maintainer-driven addition; the bound-handle rule keys on the
`{sessionID}` path parameter), ADR-0009 (unknown-variant tolerance via
generator-emitted custom converters — STJ's `UnknownDerivedTypeHandling` is
serialization-only, so the spec's `FallBackToBaseType` candidate was invalid). No
supersede ceremony (maintainer's call): the contradicting "public API" clause lived in
`AGENTS.md`'s Hybrid construction statement and was edited to the status quo; doc 06
stays a dated snapshot. Spec §12 model policies folded into ADR-0004.

## Q34: Error-channel scope — per-call only, or also client-level?

**How researched:** background agent against primary sources → doc 11; triggered by
the maintainer's initial lean toward a client-level default.
**Found:** no throw-default SDK ships a client-level error mode (Azure/SCM/OpenAI are
per-call only; AWS/gRPC/Octokit/Stripe have no switch; Elastic's client-level switch
is escalation-only in the opposite direction); FDG forbids option-dependent throwing;
upstream's client-level `throwOnError` is hey-api generator machinery.
**Decision (maintainer, aligned with the research):** per-call `NoThrow` stays the
only switch; reversal trigger recorded (additive scoped no-throw sub-view if
MCP-server dogfooding demonstrates the need).

## Q35: Pipeline/interceptor/retry architecture — whose shape?

**How researched:** background agent against primary sources → doc 12; upstream retry
reality verified in the submodule (the JS SDK ships zero retry).
**Found:** majors own retry in-core, on by default, behind a disable knob — never
foreign-retry auto-detection; sync hooks are the ecosystem shape (Azure
`HttpPipelineSynchronousPolicy`, AWS events); async delegate hooks on options have no
precedent; `Microsoft.Extensions.Http.Resilience` covers the full TFM matrix but
replays all methods by default and its timeouts kill long-lived SSE.
**Decision (maintainer):** AWS surface + Kiota backbone, no invented policy framework:
options knobs (core-owned idempotent-only retry, one disable knob, documented
StandardResilience recipe with an SSE bypass) + sync `void` per-attempt hooks + the
BCL `DelegatingHandler` chain. Also sealed in the same sweep: pagination mirror
(`SessionsCursor{Previous, Next}`) + forward-only auto-paginator; directory as
header-only (server precedence query > header verified); launcher `int? Port` with
auto-port (`--port=0` first, `TcpListener(0)` probe + bounded retry fallback);
CA1062 → `error`; process addition — the generator spec gets its own brainstorm →
grill cycle, then a testing architecture & strategy session, before `writing-plans`.

# Session 7 — 2026-08-09: grill session — generator architecture spec

Grill session (`grill-with-docs`) against the generator architecture spec with full
onboarding (CONTEXT, ADRs 0001–0009, ROADMAP, docs 00–12, both specs, upstream
`httpapi-codegen` + the JS SDK build line read at line level, PathSmith opened
file-by-file) and scripted re-verification of every `[verified]` claim. Decisions sealed
one-by-one with the maintainer; the spec was corrected in place. This entry is the chain.

## Q36: Do the spec's `[verified]` claims survive independent re-verification?

**How researched:** fresh scripts over `spec/openapi.json` (operations, unions, enums,
content types, envelope shapes, error schemas, dotted/trailing names, `anyOf`-null,
schema reachability); line-level reads of `httpapi-codegen/src/index.ts` and
`sdk/js/script/build.ts`; `CommandAppTester` located inside the extracted
`Spectre.Console.Cli.Testing` assembly.

**Found:** every headline count reproduced (188/61/127 ops, 172/1/0/0 union constructs,
513/0/104 literals, 44 = 20+17+7 error schemas, 4 SSE ops, 12/20 envelopes, 19×204,
7 dotted + 3 trailing-digit names, 16 collisions), and every upstream claim held —
including the hey-api fight (pre-gen surgery + guarded post-gen patches; upstream itself
patches `history` `limit`/`after` to numbers, corroborating `parameterTypeOverrides`).
What fell: the dup-ref breakdown (actual 22×404 + 4×400, not 23/3); the envelope taxonomy
missed `{data, hasMore}` (`v2.session.history`); the spec contains an unmodeled construct
— the JS-number encoding (`anyOf` of `number` + `"NaN"`/`"±Infinity"` literals, 11
locations, modern surface included) that the dialect wall would have refused; 13 of 472
schemas are unreachable from any path (incl. `OutputFormat1`); one of the 8 `anyOf`-null
locations is `x-effect-stream` metadata, not a model field. Repo-side: "LF enforced
repo-wide" had no enforcement mechanism (no `.gitattributes`, no `end_of_line`), and the
`.editorconfig` generated-code comment still described the pre-§7 `.g.cs` plan.

**Decision:** spec corrected throughout; LF enforcement materialized (`.gitattributes` +
`end_of_line = lf`); the `.editorconfig` §14 comment rewritten to the sealed file
mechanics; JS-number becomes a mechanical SpecIR node kind (`double` +
`AllowNamedFloatingPointLiterals`, no curation); emission scope = reachable closure
computed on every run (orphans reported, never listed statically); media-type matching is
parameter-stripped; `x-effect-stream` is carried opaque.

## Q37: Can the tolerant converter dispatch through the source-generated context without reflection — and which dispatch shape passes the wall?

**How researched:** scratchpad spike (`union-converter-spike`): an 88-variant union plus a
nested union, emitted-shape sources generated by script, the full analyzer wall
replicated (NetAnalyzers `All` + Meziantou + Roslynator + Sonar + VSTHRD, TWAE),
`JsonSerializerIsReflectionEnabledByDefault=false`; map and switch dispatch shapes built
from the same script. Triggered by the maintainer refusing a paper-only design.

**Found:** map shape (per-union static tag→type table + constant-size `Read`/`Write`):
0 warnings / 0 errors, all behavior checks pass — tag-first, tag-last (out-of-order),
unknown tag → carrier with `DeepEquals`-faithful re-serialization, nested-union dispatch,
round-trip re-emitting the discriminator — with reflection fallback disabled, proving
`element.Deserialize(type, context)` / `Serialize(writer, value, GetType(), context)` are
AOT-safe. Switch shape: its one inherent violation is MA0051 (`Read` at 96 lines); the
other 90 diagnostics were spike-emission artifacts. Side lessons: `[JsonPropertyName]` on
every property is load-bearing (the context has no naming policy); the discriminator
works as a get-only computed property; `[JsonPolymorphic]` is not needed at all.

**Decision (maintainer, after an explicit pragma-vs-shape discussion):** dispatch as data
— per-union static tag→type map, no polymorphism attributes, computed-property
discriminator; plain `Dictionary` downlevel (no `System.Collections.Immutable`
dependency). No pragma and no arbitration: the compliant shape is the natural generated
shape. Retires ROADMAP's `[JsonPolymorphic]`/`AllowOutOfOrderMetadataProperties`
downlevel unknowns. Selective suppression remains a legitimate future move for
hand-written code, through the recorded per-rule arbitration pattern.

## Q38: Does the file-based entry hold against the repo's implicit build files?

**How researched:** Microsoft Learn file-based-apps page (2026-04) in full; repo
`global.json`/`.editorconfig` inspected; STJ curation options run-verified via a
file-based script in the scratchpad.

**Found:** file-based apps inherit `Directory.Build.props`/`Directory.Packages.props`/
`global.json` — binding the entry to the repo rules is real (and deliberately opposite to
the page's isolation recommendation); the build cache is not documented to key on
`#:project` library changes (stale-tool hazard); the shebang needs the `-S dotnet --`
form plus LF/no-BOM; `dotnet run file.cs` runs a csproj when one sits in the cwd;
`dotnet format` requires an MSBuild workspace (confirming the in-memory-compare
rejection); `Disallow` + comment-skip + trailing commas compose, and `Disallow` catches
even a naming-policy mismatch.

**Decision:** §3.3 became a three-item verification list with a two-condition fallback
trigger; shebang corrected; `--verify` gained a self-checked dirty-generated-paths
precondition; the determinism claim is conditioned on the SDK feature band with a
CI-canonical rule (`latestFeature` roll-forward makes `dotnet format` a function of the
resolved SDK).

## Q39: Which sealed decisions moved, and which merely sharpened?

**Found/decided:** fingerprints split into two kinds — full subtree (method and path now
explicitly included) for exclusions, transport shape for the hand-wired SSE ops, because
hashing the event unions' closure would break on nearly every refresh and erode the
review gate (ADR-0008 aligned). XML docs: the pinned spec documents 185/188 operations
but only 3/472 schemas and 27/1836 properties — the emitter synthesizes deterministic
fallback docs so CS1591 stays `error` with no exemption. Behavior-premised curation rows
(`parameterTypeOverrides`, explicit-null `propertyOverrides`) carry mandatory `reason`
fields; their premise drift is recorded as accepted residual risk (caught by integration
tests and refresh review, not by the radar). In-schema drift of generated operations is
explicitly assigned to the refresh-PR regen diff. All 11 brainstorm seals remain
standing; none was reversed.

# Session 8 — 2026-08-09/10: brainstorm session — testing architecture & strategy spec

Brainstorm session per the 2026-08-09 handoff: full onboarding plus two background
verification agents (upstream submodule read at line level; reference-repo workflows read
from GitHub). Decisions sealed one by one with the maintainer; output:
`superpowers/specs/2026-08-10-testing-architecture-design.md`. This entry is the chain.

## Q40: How does upstream actually test itself — and does "every endpoint exercised" hold?

**How researched:** submodule at line level — `packages/opencode/package.json` scripts,
`test/server/httpapi-exercise/{index,routing,runner,backend,environment}.ts`,
`test/lib/{llm-server,cli-process,test-provider}.ts`, `test/preload.ts`, `bunfig.toml`,
`packages/sdk/js/src/{server,process}.ts`, `.github/workflows/test.yml`.

**Found:** `test:httpapi` is a route-coverage harness: the operation inventory is derived at
runtime from the server's own API definition (`OpenApi.fromApi(PublicApi)`) and diffed
against a scenario DSL list; `--fail-on-missing`/`--fail-on-skip` gate the run; three modes
(`coverage`/`auth`/`effect`); it executes **in-process** (`HttpRouter.toWebHandler` — no
spawned server, no sockets), Linux CI leg only. Upstream tests never hit a real LLM:
`TestLLMServer` (in-process fake OpenAI-compatible SSE server on port 0, scripted reply
queue incl. `hang`/`streamError`/`httpError`/`reset`, auto-`"ok"`, fixed title answer), an
ordinary provider config row (`test/test-model`) injected via `OPENCODE_CONFIG_CONTENT`,
plus a first-party VCR package (`http-recorder`) with committed cassettes; the bun preload
deletes every provider API key; CI passes no LLM secrets. Upstream's tests never use the
SDK's `createOpencodeServer` (fixed default port 4096); their subprocess fixture spawns the
real CLI with `--port 0` + stdout parsing ("Hard-coded ports flake under parallel tests")
and a full env-isolation set. No container-based test harness exists upstream.

**Decision/lesson:** upstream's in-process confidence rests on owning the router (embedded ≡
networked minus TCP) and does not transfer — the layer they skip is exactly our product, so
real-process integration is mandatory here (aligns with ADR-0001). Ported into our design:
the env-isolation set, the port-0 rule, the fake-LLM tiering, the coverage-gate idea. The
ROADMAP's "free models for determinism" assumption falls (see Q43).

## Q41: What CI/test patterns do the maintainer's reference repos actually run?

**How researched:** workflow files read from GitHub at pinned commits —
`localstack-dotnet-client` (`ci-cd.yml`, `aws-sdk-canary.yml`, badge action, Cake
`TestTask.cs`/`BuildContext.cs`, test csprojs) and `dotnet-aspire-for-localstack`
(`ci-cd.yml`, `run-dotnet-tests` composite, badge action, `Directory.Build.props`).

**Found:** both repos run three-OS matrices with `dorny/test-reporter` TRX flows;
container-backed tests are Linux-only (repo 1: a `--skipFunctionalTest` flag computed from
`runner.os` into the Cake task; repo 2: step-level `if: runner.os == 'Linux'`). Repo 1 runs
net472 on all three OSes (macOS via `brew install mono`); repo 2 carries no net472. Repo 2's
composite action queries TFMs from MSBuild and loops `dotnet test -f` per TFM with per-TFM
TRX (the MTP CLI shape our `ci.yml` already uses). Repo 1's daily canary floats dependencies
to latest with a cleared NuGet HTTP cache, non-blocking (header comment: pinned CI missed a
breaking change for 70 days).

**Decision:** three-OS direct-install legs + a Linux-only containerized clean-install lane
adopted; a nightly non-blocking canary against `opencode@latest` adopted (behavioral drift
between spec refreshes — complements the fingerprint radar, which only sees the spec at
refresh time).

## Q42: WireMock — or any mock framework — or a hand-rolled fake LLM?

**How researched:** WireMock.Net docs (faults, delays, stubbing) + web
(wiremock/wiremock#460, community answers); survey of SSE-capable mocks (MockServer, MSW)
and LLM-specific mock servers (llmock, mock-llm, AI-Mocks).

**Found:** WireMock (Java and .NET) has no SSE/streaming support — bodies are delivered
whole; delays and faults are whole-response only — so the mid-stream failure modes the
design values (hang, mid-stream cut, reset) are inexpressible. MockServer supports SSE but
is a JVM dependency; MSW is JS service-worker interception; LLM-specific mock servers exist
but each drags a foreign runtime (Node/JVM) into three-OS CI with unverified failure-mode
scripting. Below the socket, an in-memory `HttpMessageHandler` stub is strictly stronger;
above it, the real server is the point.

**Decision (maintainer):** no mock framework at any test level. The fake LLM is hand-rolled
as a **behavior port** of upstream's `TestLLMServer` (`llm-server.ts` as line-level behavior
reference at build-out; Kestrel + Channels idiom). Running upstream's own fake via bun was
rejected — a dependency on upstream's private test internals, the `opencode-mcp` trap class
(doc 03).

## Q43: The design chain — what was sealed?

**Found/decided (each sealed individually with the maintainer):** three-level backbone
(unit / contract / integration — an assurance chain with the same-source-circularity caveat
recorded and broken mechanically by the auth+reachability sweep); project layout
(`Contract/` inside `OpenCode.Sdk.Tests`; separate `OpenCode.Sdk.Integration.Tests`; **all
TFMs in integration** — maintainer decision); dual-mode harness with **in-code TUnit-native
mode selection** (a global env switch was proposed and overruled), shared per-assembly
servers with session-level isolation; own clean-install Docker image on GHCR with the
opencode version pin **single-sourced** with the spec pin (`refresh-spec` stamps it);
tool-emitted operation inventory + `[ExercisesOperation]` declarations + a hard coverage
gate (modern surface now; consumer-driven legacy joins when the MCP server lands, ADR-0005;
`Skip` forbidden on scenarios); determinism rules (no real LLM or API keys ever — correcting
ROADMAP's free-model assumption on Q40's evidence; port 0 everywhere; no sleep-based waits;
SDK retry off in tests) + quarantine policy (mandatory issue link, non-blocking CI step, no
blanket `[Retry]`); stream and launcher scenario sets (`session.history` numeric params
designated as the catch point for behavior-premised curation overrides; net472
concurrent-stream regression; fake-binary/real-binary launcher split; helper-process orphan
test); generator spec §11 sealed with three revisions (tests for the tool's two new outputs;
`refresh-spec` command tests; round-trip behavior tests reassigned to
`OpenCode.Sdk.Tests`); CI wiring (pinned opencode install on the three-OS legs, Linux
container leg, quarantine step, nightly canary; badges out); Verify limited to emitter
snapshots + the public API surface lock; coverage philosophy risk-focused with no numeric
gate. Spec: `superpowers/specs/2026-08-10-testing-architecture-design.md`. Next: a holistic
grill session (all three specs, testing-strategy focus) → `writing-plans`.

# Session 9 — 2026-08-10: grill session — holistic, testing-architecture focus

Grill session (`grill-with-docs`) across all three specs with full onboarding and **direct
re-verification of every load-bearing upstream claim** — the brainstorm session verified via
agent reports; this session read the files by eye (`package.json`, `llm-server.ts`,
`cli-process.ts`, `test-provider.ts`, `preload.ts`, `bunfig.toml`, all five
`httpapi-exercise/` files, `sdk/js/src/{server,process}.ts`, `.github/workflows/test.yml`,
the Dockerfile; reference-repo workflows re-read from GitHub). Decisions sealed one by one
with the maintainer; the testing spec was corrected in place; AGENTS.md and CONTEXT.md
gained sealed additions. This entry is the chain.

## Q44: Do the testing spec's upstream claims survive direct re-verification?

**Found:** the headline claims held (three-mode harness, in-process execution, fake-LLM
tiering, the §5.2 env-isolation set verbatim, port-0 rule, no upstream container harness,
reference-repo CI patterns). Three fell or sharpened: (1) §6's "content-based routing has
no precedent" was **false** — upstream's fake ships `pushMatch`/`textMatch`/`toolMatch`
and its tool-race test uses them exactly where reply order is nondeterministic; (2) the
fake's `/v1/responses` route serves upstream's native-`@ai-sdk/openai` test families —
the `openai-compatible` provider config the harness uses only ever calls
`/chat/completions` (opencode's prompt *and* title paths both stream through it); (3) the
exerciser's own scenarios display the declaration-vs-depth gap (`v2.integration.*` accept
500s as "exercised"; the durable stream has only a 404-missing scenario upstream — our
gap-free resume scenario is novel territory nobody integration-tests over HTTP today).
Bonus evidence: `cli-process.ts` spawns the CLI with `--port 0` (public API spec §13's
UNVERIFIED item downgraded to build-out confirmation); the exerciser sets
`OPENCODE_DISABLE_SHARE`; upstream's Windows CI disables the file watcher
(teardown-EBUSY class).

**Decision:** §6 rewritten — fake serves `POST /v1/chat/completions` only; `Wait(count)` +
`Hits`/`Inputs` and per-reply `Usage` ported day-one (the sleep-ban's instrument; upstream
calls `llmWait(1)` after every prompt scenario against teardown races); `hold`/`raw`/
`contentFilter`/`pendingTool` recorded as deliberately unported; matching stays unported
with the record corrected ("precedent exists; no order-nondeterministic scenario here").

## Q45: Does the dual-mode harness survive the container filesystem boundary?

**Found:** §5.4's "per-test temp directories ride the `x-opencode-directory` header" breaks
in container mode — host paths are meaningless in the container namespace, and seed/verify
needs a shared filesystem view. Architecture fact sharpened along the way: one `serve`
process multiplexes **per-directory Instances** (location middleware resolution:
`location[directory]` query > header > `cwd` fallback — `packages/server/src/location.ts`);
the maintainer's contrary homelab experience traces to older servers — upstream's app ships
server-compat logic that strips the header for them.

**Decision:** workspace model — per-run host root bind-mounted at `/workspace`; per-test
GUID-named subdirectories created through the fixture (`CreateWorkspace()`), exposing
`ServerPath`/`HostPath` views; tests never hand-build paths. §2 principle 4 reworded:
process management is the only *free* variable — filesystem namespace and LLM reachability
are differences *pinned by the fixture*. CONTEXT.md updated: `Instance` = per-directory
context; new `Server process` term.

## Q46: Auth sweep — through what, and how safely?

**Found:** upstream's auth mode probes with `auth_*` path placeholders and `{}` bodies,
needed per-route `.probe()` overrides (`global.upgrade` → `{target:1}`; `pty.connectToken`
→ ticket header) and a 1 s abort race for SSE; credentialed probes **execute parameterless
mutating routes for real** (upstream's own scenario proves `POST /session {}` creates;
dispose ops run).

**Decision:** probes ride `SendAsync` + `OpenCodeRoutes` (typed-method→route binding is
level 2's job; typed probes would demand mechanical construction of 188 inputs); the
operation inventory gains **HTTP method + path template** fields; a curated probe table in
test code (body/header overrides, `reason` mandatory) with an `authOnly` flag for
destructive parameterless ops; SSE probes via `ResponseHeadersRead` (no timeout
heuristics); data-driven one-result-per-op, sequential, dedicated password-enabled
instance.

## Q47: Can the gate see assert depth — and what about ops that cannot 2xx?

**Found:** declaration-based gates cannot see depth (upstream counts 500-asserting
scenarios as covered). The `integration.connect.*`/`integration.attempt.*` family needs
upstream-private SaaS backends (console proxy) for success paths — under "no API keys
ever", error-path-only is structural. Faking those backends (maintainer's paranoid lean,
WireMock floated) was pushed back and rejected on mechanism: an upstream-private,
unpinned, radar-invisible protocol is the `opencode-mcp` trap moved onto the wire, and its
CI reds would carry zero SDK signal.

**Decision:** staged **status ledger** — a test `DelegatingHandler` tallies (method, route
template, status) across the run; the closing gate asserts every modern op observed ≥1 2xx
(observation, not declaration); `ErrorPathOnly = "reason"` on the attribute exempts
structurally-blocked ops, reported visibly. Skip-ban enforcement whitelists by attribute
type (container conditional-skip, `[Quarantined]`). New §2 principle 6: **fake only
published contracts** (reversal trigger: MCP consumes integrations *and* upstream ships a
console-URL override).

## Q48: Is contract + sweep + ledger enough against same-source circularity?

**Found/decided:** the sweep breaks the loop at reachability; the ledger proves the
intentional scenario layer spans the modern success paths — and those paths inherently
exercise typed deserialization against real responses (missing `required` members and
wrong types throw). Sealed framing after the maintainer's intention-first objection:
incidental coverage counts as **defense-in-depth only, never a substitute for intentional
levels 1–2**. A generic real-response schema validator is rejected on mechanism: its
subject is upstream's spec↔server conformance, and it would turn the deliberate runtime
tolerance (ADR-0009) into CI noise. Canonicalized: AGENTS.md Testing statement grew the
**borderline-paranoid/fail-closed posture** sentence; Engineering Conventions gained the
**defensive-programming-by-default** rule.

## Q49: What does shared-server parallelism actually risk, and what does integration cost?

**Found:** upstream's harness is sequential with post-mutation resets — no precedent for
shared-server parallelism; but per-directory Instances mean per-test workspaces isolate
instance-scoped mutations (`config.update`, `mcp.add`, `project.update`, …) by
construction. Residual risk is **process-global state** only (`global.*`, the XDG auth
store, the `tui.*` queue, `sync.start`).

**Decision:** the parallelism boundary is process-global state — such scenarios use a
dedicated fixture, visible in code; candidates listed, per-op classification at build-out.
Container leg runs **net10.0 only** (its subject is the server side; client TFM
sensitivity is covered by the direct legs). Duration guardrail recorded: measure first; an
integration leg over ~15 min makes the middle TFMs smoke-set candidates — decided then.

## Q50: Pin, GHCR, and canary operational mechanics?

**Decision:** machine-readable `spec/opencode-version` (single-line file) stamped by
`refresh-spec` alongside `SNAPSHOT.md`, consumed by the CI install steps, the image
workflow, the container fixture, and the canary report. No scheduled image rebuilds, no
tag cleanup — the image refreshes at the refresh cadence. Canary failure auto-files a
`canary`-labeled GitHub issue (label-deduped comment when open) — non-blocking signal made
durable in the tracker.

## Q51: Do the six TUnit mechanisms hold on the pinned version?

**How researched:** scratchpad spike (`tunit-mechanics-spike`), TUnit 1.63.25, run on
net472/net8.0/net10.0 — all green, plus docs cross-check.

**Found:** `SharedType.PerTestSession` = exactly one instance per test *process* (a
multi-TFM assembly boots one shared server per TFM leg); `[InheritsTests]` runs
base-declared tests once per concrete subclass; custom conditional skip works via
`SkipAttribute.ShouldSkip(TestRegisteredContext)` override (lifts when the condition
clears); `[NotInParallel("group")]` serializes exactly the keyed group (active-counter
asserted) while unconstrained tests parallelize; category splits work via
`--treenode-filter "/*/*/*/*[Category=X]"`; MTP discovers `[AppName].testconfig.json` and
TUnit reads arbitrary nested keys via `TestContext.Configuration.Get` — the
`testingPlatform.environmentVariables` section did **not** apply and is not relied upon.

**Decision:** the TUnit block leaves §14; results recorded at their §5.1/§5.4 use sites.

# Session 10 — 2026-08-10: slice 0 build-out — file-based entry verification

Closing session for slice 0's tooling skeleton: ran the generator spec §3.3 three-item
verification list against the committed file-based entry
(`tools/opencode-tool.cs` → `OpenCode.Sdk.Tools` via the `#:project` directive) and
recorded the Task 3 MA0048 arbitration trace in its research home.

## Q52: Does the file-based entry survive the §3.3 verification list?

**How researched:** the three §3.3 checks run live against the entry from the repo root.
(1) Strict-props build: `dotnet run --file tools/opencode-tool.cs -- generate` — the
entry inherits `Directory.Build.props` strict props on the cached `dotnet run` path, so a
green build under the full analyzer wall + TWAE is the proof. (2) Cache staleness: with
the entry warm, the `GenerateCommand` stub message was mutated to append
` (cache-probe)` via a file-only edit (no `dotnet build` between edit and re-run), then
the entry was re-run. (3) Invocation forms: `dotnet run --file … -- generate` (any OS)
and `--help` were exercised; the Unix direct-invocation form `./tools/opencode-tool.cs`
was pinned (mode 100755, shebang) but not run on this Windows host — Linux CI runs it in
Task 5. Task 3's strict-props build at session 9 had hit MA0048 on the synthesized
`Program` behind the sealed entry filename; that arbitration is included below as the
required strict-props finding trace.

**Found (per item):**

- *Strict-props build verdict:* clean. Post-arbitration, the entry builds under the
  inherited strict props with `0 Warning(s) 0 Error(s)` and the fail-loud stub prints
  `generate is not implemented yet — the generator pipeline has not landed.` (exit 1,
  captured not misclassified). MA0048 (mezintou/Meziantou.Analyzer — "File name must
  match type name") fired on the first run because the SDK synthesizes a `Program` type
  behind a sealed entry filename no MA0048 mode (`Exact`/`Prefix`/`LongestCommonPrefix`)
   can match. Classified as a **Level 1 recorded fallback** (deviation protocol Level 1;
   AGENTS.md Hard Rule — "when a rule misfires on real code, the move is a per-rule
   arbitration comment naming the winner — never a policy rollback"). The pre-authorized
   remedy is a file-scoped arbitration (`.editorconfig`'s last section:
   `[tools/opencode-tool.cs]` → `dotnet_diagnostic.MA0048.severity = none`, comment
   naming the sealed entry contract as winner); it silences MA0048 for that file only,
   and MA0048 stays `error` for every other file (emission file = type commitment
   intact). This is a recorded fallback, not a policy rollback — the rule remains on for
   the rest of the codebase, generated output included. The §3.3 cache-staleness
   fallbacks (routed-build Level 1; console-app promotion + ADR-0003 correction Level 2)
   are independent of MA0048 and were not triggered by it: their subject is cache
   staleness, evaluated under the staleness check below. Initial placement of the
   file-scoped section silently narrowed the global `[*.cs]` scope (`.editorconfig`
   section headers are cumulative until the next header); a fix-up commit moved the
   arbitration to the file's last section so it no longer narrows any subsequent section.

- *Staleness verdict:* **no mitigation needed.** After the ` (cache-probe)` edit and
  re-running `dotnet run --file tools/opencode-tool.cs -- generate` without an
  intervening `dotnet build`, the output read
  `generate is not implemented yet — the generator pipeline has not landed.
  (cache-probe)` (exit 1) — the freshly edited `GenerateCommand` ran. The marker
  appeared, so `#:project` library changes trigger rebuilds on the file-based entry path;
  the stale-tool hazard does not exist for this entry shape. The generator spec §3.3
  routed-build Level 1 fallback was therefore **not triggered**, and the two-condition
  fallback (console-app promotion + ADR-0003 correction) stayed dormant. Probe edit
  reverted by apply_patch; the `GenerateCommand.cs` blob was bit-identical pre- and
  post-task (SHA256 `de69cb4e…`, git blob `df921b4…`).

- *Invocation forms:* `dotnet run --file tools/opencode-tool.cs -- generate` exits 1 with
  the stub message; `dotnet run --file tools/opencode-tool.cs -- --help` exits 0 with
  usage listing `generate — Regenerate the SDK model layer from spec/openapi.json`. The
  Unix direct-invocation form `./tools/opencode-tool.cs <args>` is pinned by mode 100755
  + the `#!/usr/bin/env -S dotnet --` shebang and awaits Linux CI verification in Task 5.

**Decision:** §3.3 verification list passes; the file-based entry survives as sealed.
Cache mitigation is **off** — Task 5's CI smoke step does **not** prepend a routed
`dotnet build` line; the §3.3 cache-staleness fallbacks were **not triggered**
(marker-present path: routed-build Level 1 unneeded; console-app promotion + ADR-0003
correction Level 2 dormant). MA0048 is the **one Level 1 recorded fallback executed in
this slice** — pre-authorized by the analyzer-policy Hard Rule's per-rule arbitration
procedure (a recorded fallback, not a policy rollback); the file-scoped arbitration is
the status quo for this one entry file. If the entry contract ever moves (e.g.
console-app promotion at a future cache-Level-2 trigger), the arbitration moves with it
or is deleted.

# Session 11 — 2026-08-10: slice 1 planning — parser + SpecIR plan and dialect census

Planning session for slice 1 (issue #2): the sealed generator spec §4.1 was converted
into the executable plan `docs/superpowers/plans/2026-08-10-slice-01-parser-specir.md`.
Sealing the plan required a rigorous dialect census of the pinned spec — a Python walker
mirroring the intended parser recursion exactly, so every keyword occurring at a schema
position was enumerated rather than sampled.

## Q53: Does the pinned spec fit §4.1's sealed construct inventory?

**How researched:** scripted probes over `spec/openapi.json` (v1.18.15 pin): full
keyword-frequency walk at schema positions (named schemas, parameter/requestBody/response
schemas, recursing exactly where the parser will), extension inventory, parameter
style/`in`/`required` census, media-type inventory, envelope-shape census,
`required`⊆`properties` check, ref-target and ref-sibling checks, keyword-coexistence
checks (`anyOf`+`oneOf`, `enum`+`const`, `items`+`prefixItems`).

**Found:** four constructs the §4.1 inventory did not name — a §4.1-faithful wall would
have refused the pinned spec itself: (1) `SessionDurableEvent` is the document's one
**`oneOf`** union (28 refs); (2) `Config.plugin` items contain a **`prefixItems`
tuple** (`[string, object]`, fixed arity); (3) `SessionDurableEventStream`/`V2EventStream`
are **content-encoded strings** (`type: string` + `contentSchema`/`contentMediaType:
application/json`) — the former wrapped by `v2.session.events`'s inline `{id, event,
data}` SSE media envelope, the latter currently unreferenced; (4) `v2.session.active`'s
payload is a **single-pattern `patternProperties`** dictionary. Also pinned:
`x-codeSamples` sits on all 188 operations (docs metadata), `x-websocket` only on
`v2.pty.connect`; validation keywords occurring at schema positions are `pattern`,
`minimum`, `exclusiveMinimum`, `maximum`, `minItems`, `maxItems` (the last two
tuple-relevant only at the one `prefixItems` site, plus one plain-array `minItems`).
Shape nuances: the special-value-number `anyOf` carries a fifth, *combined* literal
branch `["Infinity","-Infinity","NaN"]`; boolean literal markers exist (`healthy`,
`/global/upgrade`'s `success` true/false discrimination); `GlobalEvent` has properties
literally named `properties`/`type`/`required` (property bags must never keyword-match);
six objects carry both `properties` and an `additionalProperties` schema; every
`[verified]` §4.1 count re-checked held (0 `allOf`/`discriminator`/type-arrays, 26
duplicate-ref sites — live example `v2.session.list` 400, since `session.get` 404 is a
single `NotFoundError` ref in this pin).

**Decision:** deviation protocol level 2, caught at planning time: §4.1 corrected in
place with maintainer approval (union keyword `anyOf`/`oneOf` recorded; tuple and
content-encoded-string node kinds added; `patternProperties` recorded as a dictionary
spelling with the key pattern dropped as validation-only; extension dispositions pinned —
`x-codeSamples` known-ignored, `x-websocket` recorded, unknown `x-*` refuse). No locked
decision was touched; the two-stage pipeline, wall philosophy, and node-kind model
absorbed all four findings without structural change. The plan's wall tables carry the
complete known/known-ignored/refused sets validated by the census.

## Q54: Which filesystem packages seal the slice, and does the TestableIO analyzer join the wall?

**How researched:** NuGet flat-container/registration queries for newest stable versions
and dependency shapes; the official Testably package README and repository documentation;
restored package API surfaces; the analyzer's rule docs read from its repository
(`TestableIO/System.IO.Abstractions.Analyzers`, active, last push 2026-07); a scratchpad
strict-analyzer probe for CA1720 on the planned node-type names; TUnit exception-assert
syntax verified against its documentation.

**Found:** the active two-package filesystem setup is `Testably.Abstractions` **10.3.0**
for production and `Testably.Abstractions.Testing` **7.0.2** for tests. Both implement the
shared `System.IO.Abstractions.IFileSystem` contract; production supplies
`Testably.Abstractions.RealFileSystem`, and tests supply
`Testably.Abstractions.Testing.MockFileSystem`.
`TestableIO.System.IO.Abstractions.Analyzers` newest stable **2022.0.0** (maintainer
proposal): rules IO0001–IO0011 all default-enabled at Warning — TWAE escalates them, so
no `.editorconfig` section is needed; **IO0006 covers `Path`**, which caught the plan's
own smoke test using `Path.Combine` directly (fixed to `fileSystem.Path.Combine` before
sealing). CA1720 does not fire on compound type names (`ObjectNode`,
`ContentEncodedStringNode` shapes probed clean). TUnit's
`await Assert.That(action).Throws<T>()` returns the exception for follow-up asserts.

**Decision:** three filesystem CPM pins for slice 1 — the Testably production/test pair
plus the IO analyzer at 2022.0.0. The analyzer remains an independent package and is wired
repo-wide in `Directory.Build.props` like its seven siblings (fail-closed maximalist
posture: the shared filesystem seam is mechanically enforced, not review-enforced). The
analyzer is an older-Roslyn build; if the current compiler refuses to load it, the plan
marks that a stop-and-report finding, never a silent drop.

# Session 12 — 2026-08-11: redesign research — Microsoft.OpenApi ingestion

Research/design session per `HANDOFF-2026-08-10-2` (the Task 10 stop). Five-mode scratch
probe in the evidence worktree (`.scratchpad/OpenApiProbe/` — baseline / inventory / wall /
projection / ops; outputs preserved at `.scratchpad/probe-outputs-2026-08-11.md` there);
upstream codegen read at line level in the submodule; live remote inspection of
`anomalyco/opencode` branches. Decisions sealed with the maintainer in-session; canonical
docs corrected in the same change. This entry is the chain.

## Q55: Why did Slice 1's Task 10 stop, and was the planning census complete?

**Found:** the pinned spec contains unrestricted JSON Schemas written as `{}`; the custom
parser's sealed dispatch refused them and dropped their parents, producing a 44-error batch.
The exhaustive DOM+raw-walk correlation then found **19** empty-schema sites, not the 6 the
handover's grep-based count claimed — the second census-method failure after session 11's
zero-key miss: pattern-matching counts miss what only an exhaustive positional walker sees
(one site is even a union branch, `Workspace.extra/anyOf/0`; several are semantically
load-bearing: tool results, `AssistantMessage.structured`).

**Decision:** unrestricted `{}` becomes an explicit any-value node (mapping it to a
free-form *object* would silently narrow the wire); census claims over the spec are made
only by exhaustive walkers, never by grep.

## Q56: Can Microsoft.OpenApi replace the hand-written parser?

**How researched:** the handover's investigation list executed as scratch prototypes over
the pinned spec: exhaustive DOM inventory correlated against a raw `JsonNode` walk;
whitelist wall visitor + doctored-spec injections + a `ValidationRuleSet` comparison; a
minimal projection implementing every Task 10 landmark; fragment-API, stream-ownership,
determinism, cycle and cost probes; NuGet version re-check.

**Found:** the library reads the pin with 0 errors/0 warnings. The correlation closes the
loss question: every raw keyword is either typed by the DOM or retained in
`UnrecognizedKeywords` — the only untyped pinned keyword is `prefixItems` (1 site). The
library types the full JSON Schema 2020-12 applicator vocabulary (injected `if`/`then` land
as typed members, **not** unrecognized keywords), so a fail-closed wall must be
whitelist-shaped over admitted typed members. A ~180-line DOM visitor passes the pin clean
and catches 7/7 injected foreign constructs batched and located while the library accepts
them; `ValidationRuleSet` also works (default set + `typeof(IOpenApiSchema)` key, decent
pointers) but the single visitor is simpler — the default rule set stays on as a free extra
radar. A ~240-line projection answers all Task 10 landmarks 19/19, with typed reference-ID
access (`OpenApiSchemaReference.Reference.Id` — the spike's reflection was unnecessary) and
deterministic repeated ingestion (SHA-256-stable, including `x-effect-stream`
serialization). `OpenApiModelFactory.Parse<OpenApiSchema>` parses the raw `prefixItems`
items with host-document context. `LeaveStreamOpen` is honored (Testably seam intact).
Iteration order is deterministic across loads and equals document order (observed behavior,
not a contract — outputs still sort). No ref cycles in the pin (deepest `Target` chain 15);
parse cost 72 ms / 26 MB. 3.9.0 is the newest stable. Direct-Binder input (no SpecIR) was
compared and rejected on the same runs: the reference/concrete dichotomy and
flags-and-defaults idioms leak into every rule, the same analyses re-traverse the DOM,
every Binder test needs reader-built fixtures, and the refresh diff has no stable model to
serialize.

**Decision (maintainer):** Microsoft.OpenApi (pinned) becomes the tooling reader; the
generator owns a whitelist-shaped fail-closed projection into a minimal SpecIR (the name is
kept — renaming to "Contract" would collide with the level-2 contract-test area); ADR-0003
is revised in place rather than superseded (early-stage call — ADRs stay changeable);
library-upgrade tripwires guard the wall (`prefixItems`-still-unrecognized + a typed-member
inventory snapshot); the custom parser is retired — its branch/worktree
(`feature/slice-01-parser-specir`) is kept as an evidence reference.

## Q57: Did upstream write its own OpenAPI parser for the JS SDK?

**Found:** no. The published SDK delegates OpenAPI parsing to `@hey-api/openapi-ts` and
wraps it in two fail-safes: pre-generation document surgery (`httpapi/public.ts`
`matchLegacyOpenApi` — including stripping single-element `allOf` wrappers, which is why
the published artifact censuses at 0 `allOf`; `sdk/js/script/build.ts` deletes unreachable
`SessionNext*1` schemas via a reachability walk) and post-generation patches that are all
assertion-guarded — every `.replace()` throws when it no longer matches, so each patch
doubles as a drift detector (the numeric `after`/`limit` patch rests on the same premise as
our `parameterTypeOverrides` row). Upstream's own codegen (`httpapi-codegen`) bypasses
OpenAPI entirely: it compiles the Effect contract into its own IR behind semantic
`GenerationError` refusals (name collisions, multiple payload/success schemas, unsupported
encodings, errors without a literal discriminator, refuse-until-implemented recursive
types).

**Lesson:** upstream's own practice matches the redesign's shape — delegate document
parsing, own a fail-closed semantic wall. The assertion-guarded-override pattern is worth
porting (our behavior-premise integration catch points and fingerprint pins are its
equivalents).

## Q58: Is the next major changing the spec dialect?

**How researched:** `git fetch` in the read-only submodule (worktree untouched, pin
intact); branch inspection via `ls-remote`/`log`/`show`; a keyword census over three
artifacts — the pin, `origin/dev:packages/sdk/openapi.json`, and
`origin/v2:packages/protocol/openapi.json` (copies + census under
`.scratchpad/v2-sneakpeek/`); npm dist-tags.

**Found:** the `2.0` branch is still the frozen April ancestor (doc 10 holds). The active
successor is branch **`v2`** — diverged from `dev` 2026-06-26, daily commits, monorepo
restructured (no `packages/opencode`/`packages/sdk`; `core`/`server`/`client`/`sdk-next`
instead), with the OpenAPI artifact moved to `packages/protocol/openapi.json`. Its dialect:
104 paths / 120 ops / 322 schemas; operationIds still `v2.`-prefixed; **422 `allOf`
occurrences — every one a single-element wrapper carrying only validation keywords**
(`{"type":"integer","allOf":[{"exclusiveMinimum":0}]}`; arity distribution 100% one) —
consistent with the legacy-compat strip not running on this artifact; 0 `const` (the April
const shift reverted — literals are single-value `enum` again, 359 sites); 0 type-arrays;
`prefixItems` 6; `patternProperties` 2; empty schemas 2; `x-effect-stream`/`x-websocket`
present; component names now widely dotted (`Location.Info`). `dev`'s spec censuses
identical to the pin (nothing urgent on the 1.x line; npm `latest` is 1.18.16). Run against
the v2 artifact, the wall prototype reports 428 located errors — all `allOf` — a live
demonstration of the drift radar: the future admit-rule class is already known
(single-element validation-only wrapper) and lands as a deliberate wall extension when a
refresh needs it.

**Decision:** no plan deviation now; a v2-watch item joins ROADMAP Open Questions; the
evolvability posture is the wall + tripwires + nightly canary, with the integration suite
as the primary guard.

# Session 13 — 2026-08-11: grill session — ingestion redesign (LLM-to-LLM)

Fresh-context adversarial grill of the corrected generator spec §4/§4.1, run as a
background agent driven from the redesign session (the maintainer's cross-session-review
model: agents attack, the maintainer seals). The agent extended the evidence-worktree
scratch probe with modes `members`/`hostloss`/`hostbisect`, re-ran every session-12 probe,
and attacked with independent walkers; the orchestrating session re-verified the two
heaviest findings first-hand before relaying. 11 findings; three maintainer decisions
sealed in-session; the spec was corrected in the same change. This entry is the chain.

## Q59: Does the redesigned §4.1 survive adversarial verification?

**Found:** the architecture held — no locked decision contradicted, 15 session-12 claims
re-verified — but the sealed text did not:

1. The wall/tripwire covered one DOM host type of seven, while the pinned library types
   dangerous constructs silently at every level: path-level `parameters` (a projection
   reading only operation parameters silently drops a real parameter), `headers`/
   `callbacks`/`webhooks`, media `ItemSchema` (OpenAPI 3.2 members already shipped in
   3.9.0 — populated with zero diagnostics under a `3.2.0` version string, which itself
   parses with no signal; the DOM does not retain the raw `openapi` string), content-based
   parameters, non-deepObject styles, `$defs`/dynamic anchors.
2. A boolean property schema (legal JSON Schema 2020-12) crashes `LoadAsync` with a raw
   `NullReferenceException` before any diagnostic — an unhandled reader-crash class.
3. Unknown non-`x-` keys at non-schema hosts surface only as reader diagnostics (empty
   pointer, location in the message text) while the DOM silently drops the key — reader
   diagnostics must fail generation, not merely be "surfaced".
4. §9's fingerprint promise ("everything stays on the radar") needed wire-faithful
   subtrees the minimal SpecIR deliberately does not carry (898 `pattern`/75 `minimum`
   dropped as known-ignored).
5. Envelope-shaped *named* schemas (`SessionsResponse`, `SessionHistory`,
   `SessionMessagesResponse`) alias the reachable closure: emitted as models while
   `EnvelopeEmitter` synthesizes the same shape.
6. Field-inventory gaps against the design's own promises: property/schema `description`
   (135 sites — the XML-doc pipeline's input), the six hybrid objects, `format`'s
   disposition, and `const` string-typing in the DOM (`const: 42` arrives as `"42"`).
7. The marker-keyed promotion rule is not universal — 5 pin sites are heterogeneous
   structural unions without markers (`Config.formatter` `bool | dict` et al.), which
   also cannot take ADR-0009's tag-dispatch converters; the marked/structural distinction
   must be a recorded SpecIR fact.
8. `OpenApiSchemaReference` proxies members to its target *except* sibling annotations
   (`Description` returns the local text) — a merged-view trap for any code holding
   `IOpenApiSchema`.
9. "The projection is the only code that touches library types" had no enforcement
   mechanism (BannedApiAnalyzers is compilation-scoped; a folder-scoped ban is
   inexpressible).

Two session-12 claims fell (recorded here, not rewritten there): the v2-branch wall run
reports **422 `allOf` + 6 relocated `prefixItems`** sites, not "428 — all `allOf`"; and
2 of the 422 single-element wrappers carry `description` alongside validation keywords —
the future admit-rule must tolerate annotations and preserve their doc text. The grill
also re-demonstrated the session-11/12 census lesson on itself: a key-frequency count of
v2 shows `title`/`default` "populated", but all sites are properties literally named
`title`/`default` — census claims stay walker-based.

**Decisions (maintainer):** fingerprint source = ingestion-computed raw-content hashes
per operation and per named schema, carried opaque on SpecIR — the Binder composes and
persists, never re-reading the spec; envelope-classified response-root schemas are not
emitted as models (only payload/cursor/location subtrees join the closure; a non-envelope
inbound reference is a batched error); the DOM boundary is enforced by two guard tests
(SpecIR public-surface reflection + a source scan for library `using`s outside
`Generator/Ingestion/`), keeping the sealed single-project layout. Spec corrections
applied in the same change: per-host admitted/known-ignored/refused wall tables, the
`SpecificationVersion == OpenApi3_1` gate, reader diagnostics and translated reader
exceptions as hard wall layers (boolean-schema red test; candidate upstream bug report),
tripwires extended to every consumed DOM type plus fresh-instance defaults plus the
serialized SpecIR-of-the-pin snapshot, "unrestricted" redefined as "no admitted
constraint member populated", `const` admitted only on string-typed schemas, union
marked/structural classification, promotion index-fallback + JSON-pointer escaping, and
the honest ordering statement (member order is document order under the version pin,
guarded by the snapshot).

## Q60: Did the corrections survive the verification round?

**How researched:** the corrected spec went back to the same grill agent (context intact)
for a targeted verification pass — one verdict per demand, a fresh-eyes sweep of the
rewritten §4.1, and three new run-checks (HTTP-method census, deepObject `explode` values,
`$ref`-sibling landing behavior).

**Found:** 8/10 demands SATISFIED outright, and the rewrite itself had introduced four
findings — fresh-context review earning its keep twice in one day: (1) the catch-all
refuse rule collided with library bookkeeping members (`BaseUri`/`Self`/`Workspace`/
`Metadata` are populated on every load and appear in no table); (2) the `$ref`-sibling
refusal had no stated detection channel — typed members cannot distinguish a local
sibling from the proxied target value, so detection must ride the raw key-scan ingestion
already performs for the raw-content hashes; (3) the parameter table pre-admitted
`in: header` with zero occurrences in the pin (99 path / 319 query) and in v2 — against
the whitelist philosophy of admitting on first need; (4) §4.2/§6 still claimed universal
tag-dispatch converters while §4.1's new marked/structural distinction leaves five
reachable structural-union sites without an implementable emission shape. Verified in
passing: both artifacts use exactly the five admitted HTTP methods, and every deepObject
parameter carries `explode: true` in both.

**Decisions (maintainer):** the three wording fixes applied in the same change
(bookkeeping-member clause, raw key-scan detection clause, `header` dropped from the
parameter table); structural-union emission shape **deferred** — §4.2 narrowed to "all
*marked* unions" and §15 carries the open item (`JsonElement` carrier vs generated
wrapper type, decided at slice 2/3 planning as an API review). Verification verdict:
sealable — the redesigned §4.1 stands.

# Session 14 — 2026-08-11: overengineering grill — tooling host and testing scope

Pre-execution adversarial review of all three design specs, the slice map, and the Slice 1
plan. Three fresh-context model lineages (Grok 4.5, GLM 5.2, Kimi K3) independently read the
same evidence set: the live repository, sessions 12–13, the engineering canon, the retired
parser evidence map, the maintainer's PathSmith hosting/DI model, and the external scenario/
builder references. The maintainer then resolved disagreements one decision at a time.

## Q61: Is Slice 1 overengineered, and does its tooling foundation match the required shape?

**Found:** the ingestion design is not broadly overengineered. Reader red tests, per-host
wall tests, the DOM member/default inventory, the `prefixItems` tripwire, both DOM-boundary
guards, the SpecIR-of-the-pin snapshot, canonical-hash units, and landmark smoke each guard
a distinct failure channel demonstrated in sessions 12–13; removing one would create a
specific silent-loss window. Slice 1 is one coherent dependency chain — partial projection
without orchestration/tripwires is not a useful handoff to Binder. Two local defects did
surface. First, Task 1 called `IFileSystem` registration a DI foundation while omitting the
full hosting topology the reference and coding style require: console seam, logging,
global settings, interceptor, core/application registrations, and one production/test
composition path. Second, the plan converted nearly every five-line builder variation into
a named scenario subclass/file, turning reusable infrastructure into ceremony. Task 8's
small-document repeated-ingest assertion was the sole duplicate: Task 10 repeats the same
code path over the full pin, while Task 8's hash properties remain independently valuable.

**Decisions (maintainer):** Task 1 takes the PathSmith hosting/DI topology completely — one
`ToolApp.CreateServices` root behind `DependencyInjectionRegistrar`/`CommandApp`, options,
`IFileSystem`, `IAnsiConsole`, core and application services, global settings, and an
`ICommandInterceptor`. Logging is intentionally adapted rather than transplanted:
Microsoft.Extensions.Logging `ILogger<T>` with Spectre and optional Testably-backed file
providers replaces PathSmith's custom logger; tests override seams after the production
registrations and never copy the service list. Test setup becomes lambda-first through one
central scenario mechanism and domain builders. A named scenario class is promoted only for
cross-class reuse, non-trivial fixture-plus-shaping, or durable cross-slice domain/landmark
identity. Every planned test case and the full four-command gate after every task remain.
The duplicate Task 8 determinism assertion is deleted; the Task 10 pin-level repeat stays.
Slice 1 remains one slice and one PR, split only on measured mid-flight growth under the
deviation protocol.

## Q62: Which later testing mechanisms are infrastructure, and which are excessive breadth?

**Found:** the level-2 generated fixtures and level-3 mechanisms answer different questions.
Contract fixtures bind generated methods, routes, serializers, envelopes, and every declared
error without hand-authored per-operation cost; consumer-driven legacy depth therefore does
not justify leaving shipped legacy bindings untested. The 61/61 declaration plus observed-2xx
ledger is a breadth gate, not a requirement for 61 isolated deep stories: one workflow can
exercise several operations, while deep state assertions belong on streams, launcher,
errors, permission/question flows, and stateful mutations. The auth sweep remains the
counterparty-real reachability break in the same-source loop. Direct/container duplication
has value only where process, workspace/filesystem, stream, or basic clean-install behavior
can differ. Fake-LLM, quarantine, and canary designs are already bounded by explicit growth
triggers and non-blocking lanes.

**Decisions (maintainer):** level-2 fixtures cover both modern and legacy shipped surfaces.
Modern integration retains the hard declaration + observed-2xx gate with reasoned
`ErrorPathOnly`; workflow grouping and risk-based depth prevent ceremonial per-operation
tests. Dual-mode is selective, not the whole catalog copied twice. Direct integration runs
net472 on Windows and net8.0/net9.0/net10.0 on all three OSes. The maintainer explicitly
overrode the prior net10-only container decision after its duplication cost was challenged:
the selected Linux container suite runs in full on net8.0, net9.0, and net10.0; net472 has no
container leg. The recorded ~15-minute measurement guard remains the only trigger for later
TFM trimming. The fake LLM stays limited to the published `/v1/chat/completions` contract and
currently required scripted behaviors; quarantine/skip discipline and the label-deduped
nightly canary issue flow remain as designed.

# Session 15 — 2026-08-11: course correction — checkpoint absorbed, lean close-out

The maintainer reviewed the Slice 1 branch outcome (8.6K lines, no SDK output) together
with the complexity checkpoint (research doc 13) and redirected process, assurance policy,
and sequencing in one pass. Decisions sealed in-session; the branch was then closed lean in
the same session. This entry is the chain.

## Q63: Where did Slice 1's budget actually go, and which failure causes hold?

**How researched:** per-area line accounting over `master...feature/slice-01-ingestion-specir`
(git numstat bucketed by path); the checkpoint's probes re-read; the maintainer's two
hypothesized causes tested against the numbers.

**Found:** 8,593 added lines split into generator production 3,372 (≈1K of it surveillance
walls), test-infrastructure DSL 1,472, tests 2,159, docs 1,075, hosting 299 — before Binder,
emitters, or any SDK source existed. Both maintainer causes confirmed, with the mechanisms
named: (1) fail-closed maximalism was transplanted from the analyzer wall — where it is
cheap, rules pre-exist and misfires cost one arbitration comment — onto the reader DOM,
where a whitelist wall's cost scales with the *library's* surface, not with product risk;
(2) the slice map was a layer cake in vertical clothing — first compiled SDK code at
slice 3, first callable client at slice 5, first real request at slice 7 — so the
learn-what-works moment sat behind maximal investment. A third cause joined them: paper
grill sessions ratchet monotonically — every discovered risk gets a mechanism because
nothing on paper answers "what does this cost in code?" — and assurance intensity was never
scaled by blast radius (repo tooling got the shipped-SDK treatment). The checkpoint's
absorption and Kiota wire-fidelity probes also stand as the counter-evidence that the
architecture itself was right: the projection absorbs the full pin and no OSS generator
preserves this dialect.

**Decisions (maintainer):** red lines kept — the analyzer wall and the
Testably/`IFileSystem` seam with its enforcement analyzer stay untouched; `Microsoft.OpenApi`
stays the reader. Everything else lightened: git diffs of committed generated output plus
the test suites become the primary drift radar, with targeted validators only at known
lossy seams.

## Q64: What was cut, folded, and kept in the lean close-out?

**Found/decided (keep/drop list approved item by item, then executed on the branch):**
**Dies** — `HostMemberWhitelist<T>`/`SchemaMemberWhitelist` reflection surveillance, the
document wall, fatal handling of typed annotation/validation members (`title`, `default`,
`readOnly`, `examples`, length/bound keywords — now silently ignored: known vocabulary that
cannot change emitted behavior), fatal unknown `x-*` (ignored; a generation-report line
lands with the real `generate`), the DOM member/default inventory, the SpecIR-of-the-pin
Verify snapshot, raw content hashes and `SpecOperation.RawContentHash` (no consumer;
returns with the fingerprint feature), the per-keyword red-test inventory, and the
23-landmark census (trimmed to ~10 representative landmarks). **Folds** — path-level
parameters, unknown HTTP methods, parameter location/style/content, multi-media
bodies/responses, and non-integer status keys survive as explicit typed-member checks
inside the projectors (semantic loss ⇒ wrong wire). **Stays** — reader gate (version,
diagnostics-as-errors, crash translation), the semantic projection core, unrecognized-raw-
keyword refusal (the one construct nothing downstream can ever see) with the admitted
`prefixItems` site, the `$ref`-sibling raw scan and dangling-reference sweep, the
`prefixItems` library-upgrade tripwire, both DOM boundary guards, determinism, and batched
located errors. Post-surgery evidence: the ingestion seam (`ISpecIngestion`) still absorbs
the complete pin — 188 operations (61/127), 1,501 graph nodes, all landmarks green,
repeat-deterministic — at −851/+131 for the deletion pass.

## Q65: What replaces the spec-and-slice-map process?

**Decisions (maintainer):** the three design specs and the walking-skeleton design are
demoted to vision/reference (banner added; the sealed surface is the ADRs plus `AGENTS.md`
— its two edits: the vision/reference framing and blast-radius-scaled assurance). The
deviation protocol's level 2 narrows to canonical documents; vision-doc contradictions are
level-0 notes. The slice map is retired; `docs/ROADMAP.md` now carries a six-item
deliverable-first milestone list (M1 walking skeleton → M6 operational closure) with
just-in-time 1–2-page plans. The first deliverable is sealed: `v2.health.get` +
`v2.session.message` through the full pipeline into a callable client, demonstrated once
by hand against a real `opencode serve` — deliberately process-free (no launcher, no
harness, no CI leg; the demo output rides the PR description). Consumer-pull is the
standing rule: every SpecIR fact, mechanism, and test names a consumer or a concrete
failure it prevents.

# Session 16 — 2026-08-13: M1 Arc B planning

## Q66: Is the session handle a mechanical fact or an API-policy decision?

**Found:** OpenAPI says only that `sessionID` is a required path parameter. It cannot imply
that consumers should obtain a `SessionClient`, keep the ID on that handle, or omit the ID from
operation signatures. Hard-coding `{sessionID}` or `SessionClient` in an emitter would make
curation incomplete and turn breadth into endpoint-specific branches.

**Decision (maintainer):** modern group curation may declare the collection client, bound
handle, and handle path parameter. The Binder applies that declaration as one general
partial-application rule; emitters consume final plans and know no operation IDs, wire groups,
or concrete clients. The legacy surface remains flat.

## Q67: Which acronym casing should generated C# identifiers use?

**Found:** the prior two-letter-uppercase rule emits `ID`, `SessionID`, and `CallID`, while the
maintainer wants one predictable PascalCase convention and the first API baseline has not yet
shipped. Wire fidelity is independent because `[JsonPropertyName]` retains the source spelling.

**Decision (maintainer):** normalize acronym tokens with ordinary PascalCase regardless of
length (`Id`, `SessionId`, `CallId`, `Url`, `Api`); exceptional brand spelling remains curated.
Apply the change before approving the first public API baseline.

## Q68: Should optional collection properties become nullable to model deserializer input?

**How researched:** checked `SessionMessageAgentSwitched.metadata` in the pin and ran a
source-generated System.Text.Json 10.0.11 probe with `RespectNullableAnnotations=true`, init-only
record properties, `[AllowNull]`, and a nullable-input normalization helper.

**Found:** optionality and nullability are independent. The pin omits `metadata` from `required`
but declares `type: object`, so absence is valid and explicit null is not. STJ's record path may
feed an absent optional collection through the init path as null; `[AllowNull]` removes the C#
warning but also admits explicit JSON null. A non-null property delegating to a nullable-input
helper preserves empty-on-absence while STJ rejects explicit null before assignment.

**Decision (maintainer):** optional non-null collections remain non-null in public C#. Normalize
absent values to immutable empty collections through an internal nullable-input helper, preserve
defensive copies, and reject explicit null unless the schema itself admits null.

# Session 17 — 2026-08-13: v2 retarget research

The maintainer opened the direction question before Arc B implementation: is the dual-surface
v1 investment worth its complexity while upstream's investment moves to v2? Live inspection of
`anomalyco/opencode` branch `v2` (head `1288161`, retrieved 2026-08-13) via shallow clone,
GitHub API, npm registry dist-tags, and `update.opencode.ai` probes, plus a live install and
run of the v2 CLI on this machine. Platform detail is canonical in
`15-opencode-v2-platform.md`; decisions were sealed with the maintainer in-session and the
canonical documents corrected in the same change. This entry is the chain.

## Q69: Is the dual-surface v1 investment still justified given upstream's v2 line?

**How researched:** the v2 branch tree read at file level (server handlers, protocol groups,
TUI dependencies, CLI command specs, desktop wiring); commit cadence via the GitHub API; the
ROADMAP v2-watch item's session-12 censuses re-run against today's head.

**Found:** the legacy surface's source is gone — `packages/opencode` does not exist on the v2
branch; the restructured monorepo (`core`/`server`/`client`/`protocol`/`sdk-next`) serves only
the protocol surface (30 server handler files ↔ 30 protocol groups, 1:1). The TUI runs on the
protocol-derived `@opencode-ai/client`, not the dual-generation `@opencode-ai/sdk`. A v1→v2
session-history migration is being built in code (`v2.experimental.migration.v1.status`:
"Return the progress of the V1 to V2 session history migration"). Commit volume on 2026-08-13
alone: 15 (features, fixes, tests, docs), while `dev` — the default branch — carries 1.x
maintenance. The two-endpoint-surface problem the SDK's dual-surface design absorbs is a
v1-line artifact; v2 has one surface.

**Decision (maintainer, sealed): retarget — the SDK targets the v2 protocol surface only; the
legacy surface is never built.** ADR-0005 is revised in place (the ADR-0003 precedent), not
superseded. M1 Arc B continues with a prepended retarget task (pin swap + wall admit +
regeneration). The staged alternative — finish Arc B on the 1.18.15 pin, retarget at the M2
boundary — was weighed and rejected: after the live-server demonstration (Q72) removed the
"no real v2 server to demo against" cost, staging would only buy double generation, a double
API-baseline review, and a walking-skeleton demo against a surface already declared dead.

## Q70: What is the v2 spec artifact, and how far has the surface moved since our pin?

**Found:** `packages/protocol/openapi.json` — 104 paths / 120 operations / 324 schemas, all
mounted under `/api`, operationIds still `v2.`-prefixed (the prefix-strip naming policy
carries over). The artifact is maintained: `generate-openapi.ts --check` is upstream's own
regen-verify, "chore: generate" commits land daily, and the CLI's `api` command resolves
OpenAPI operationIds against it. Versus the pinned 61-op modern block: 8 operations dropped —
including the durable-stream trio `v2.session.events` / `v2.session.history` /
`v2.session.messages` — and 67 added; the new families (`mcp` 6, `config`, `vcs` 3,
`project` 3, `shell` 6, `websearch`, `generate`) absorb what doc 10 enumerated as the 78-op
legacy capability gap, and `tui.*` is gone as a remote-control surface. Dialect census: 420
`allOf` occurrences, every one a single-element validation-only wrapper; 0 `const` (literals
back to single-value `enum`, 370 sites); 446 `anyOf`, 0 `discriminator` — the union machinery
and ADR-0009 carry over unchanged; dotted component names; `v2.session.log` (`after` +
`follow` parameters) replaces the durable stream; `v2.pty.connect` carries `x-websocket`.

**Decision:** the wall admit rule for the single-element validation-only `allOf` wrapper class
lands in the Arc B retarget task; the M3 stream design is re-derived against `session.log`
when M3 starts.

## Q71: Does opencode v2 ship its own MCP server?

**Found:** no. The protocol `mcp` group (list/add/remove/connect/disconnect/resource.catalog)
manages *configured* MCP servers — opencode as MCP host/client (`@modelcontextprotocol/sdk`
1.29.0; `packages/core/src/mcp/{client,stdio,oauth}.ts` are all client-side). The
self-exposure protocol upstream chose is ACP, not MCP: `opencode acp` — "Start an Agent
Client Protocol server" — targets editor embedding.

**Decision:** the MCP-server premise (research docs 03/04) stands on v2 — the gap this
repository's second deliverable fills remains open. ACP is recorded as an adjacent surface to
watch: it may absorb some drive-opencode-from-another-agent use cases.

## Q72: How is v2 distributed, and what is its server model?

**How researched:** npm registry dist-tags for the scoped packages; `update.opencode.ai`
channel feeds; GitHub releases of `anomalyco/opencode-beta`; CLI/client/desktop source read at
file level; then a live install and run on this machine.

**Found:** distribution is two-channel and bypasses the old `opencode-ai` package: the CLI
ships as `@opencode-ai/cli@next` (0.0.0-next-17403, published 2026-08-13), installing
side-by-side as `opencode2`; the desktop beta rides `update.opencode.ai` (channel feeds,
OIDC-authenticated publish) over `anomalyco/opencode-beta` GitHub releases
(0.0.0-beta-17406, same day), bundling its own CLI. Installation starts no server. The server
model is a shared per-user background service: a 0600 registration file in the XDG state
directory (`{id, version, url, pid, password}`) is the complete discovery contract;
`Service.ensure` reuses a healthy compatible server, replaces a version-mismatched one, and
otherwise spawns detached contenders until one wins registration. Explicit modes: `serve`
(foreground), `serve --service` (managed daemon, `service start/stop/status/restart`),
`serve --stdio` (embedding: JSON `{url}` handshake on stdout, stdin as liveness leash),
`--standalone` (private server), `--server <url>` (remote). `pair` prints server URLs + Basic
username `opencode` + password (+ QR) — the v1 auth model unchanged. Live verification on
this machine: `opencode2 service status` → `stopped` after install; `opencode2 serve
--hostname 127.0.0.1 --port 41999` up; unauthenticated `GET /api/health` → 401;
authenticated → 200 `{"healthy":true,"version":"0.0.0-next-17403","pid":…}`;
`GET /api/session?limit=2` → `{data:[…]}` containing this machine's real session history (the
central store reads existing opencode data).

**Decision:** the launcher milestone (M4) maps to `opencode2 serve`; connect-or-launch fits
the discovery-file contract; whether the SDK adopts `location[...]` query addressing in place
of the v1 `x-opencode-directory` header is decided inside the retarget task.

**Documentation pass (this session):** `15-opencode-v2-platform.md` created as the canonical
v2 platform picture; docs 09/10 corrected in place (resolved UNVERIFIED items, stamped
consequence updates); ADR-0005 revised in place; `AGENTS.md`, `docs/ROADMAP.md`, and the Arc B
plan updated to the single-surface v2 target.

# Session 18 — 2026-08-13: M1 Arc B execution decisions

An implementation session, not a research sweep: Tasks 1–4 of the Arc B plan landed and M1
closed with a live demonstration. This entry records only the decisions sealed along the way;
execution detail lives in git and the (now consumed) Arc B plan checkboxes.

## Q73: Are envelope payload names curated per operation or derived mechanically?

**How researched:** the sealed API design (payload names from a fail-closed curation map,
§5.1/§8.5) collided with the maintainer's retarget-era "no taste curation" convention when the
bare `ServiceHealth` payload needed a name; both models were priced against the ~100
payload-carrying operations of the full v2 surface.

**Found:** the mechanical candidate — the operation's subject tokens (identifier segments
after the group that do not restate the HTTP method; the group name when none remain) —
reproduces both sealed M1 values exactly (`Message`, `Health`), and a fail-closed collision
check against the response spine (`Error`, `IsError`, `RawBody`, `Status`, the response type
name) preserves the forced-review property for the cases that actually need a human.

**Decision (maintainer, sealed):** payload names derive mechanically;
`envelopePayloadNames` demotes to an override ledger for spine collisions and C#-illegal
results only; a collision fails generation until an override lands. The required-per-operation
validation retired with it.

## Q74: What may the User-Agent version token contain when the informational version is unusable?

**Found:** SourceLink appends `+<commit>` to `AssemblyInformationalVersionAttribute`; a
missing or unparsable attribute is possible in exotic build contexts, and the handoff banned
a silent assembly-version substitute.

**Decision (maintainer, sealed):** strip build metadata after `+`; when the remainder is
missing or fails `ProductHeaderValue` parsing, omit the version token entirely
(`OpenCode.Sdk` alone) — never a silent fallback, never a construction failure
(`UserAgentPolicy`).

## Q75: What shape do generated route members take?

**Decision (maintainer, sealed):** `OpenCodeRoutes` nests one static container per operation
group (client name, or the Pascal group for root placement); parameterless operations emit a
`const`, parameterized ones a `<Member>Template` const plus an escaped `<Member>(...)`
builder; members never restate their container (`Health.Get`, `Sessions.GetMessage`). CA1034
is arbitrated for the one generated file. Route member names ride the plan
(`RouteContainerName`/`RouteMemberName`) so emitters stay name-blind.

**Documentation pass (this session):** ROADMAP moved to M1-complete/M2-next; the Arc B plan's
checkboxes closed in the same commits as their code; the consumed handover was deleted and a
fresh one (`HANDOFF-2026-08-13-3.md`) records the ship/M2/horizon queue.

# Session 19 — 2026-08-14: M1 ship, review triage, M2 opening decisions

PR #16 merged after a verified multi-agent review cycle (blocker set fixed red-test-first
on the branch; every surviving finding milestone-anchored in issues #17–#25) and the
BenchmarkDotNet performance suite landed with baselines. This entry records the decisions
sealed while opening M2; execution detail lives in git and the M2 plan.

## Q76: What shape does the generated query surface take?

**How researched:** the four M2 operations extracted from the pinned spec (session.list
carries 9 optional `anyOf [T, null]` query parameters; both list operations share the
`limit`/`order`/`cursor` trio and the `{data, cursor:{previous,next}}` envelope); Azure SDK
.NET design guidelines fetched live ("DO use the options parameter pattern for complex
service methods"; `Pageable<T>`/`AsyncPageable<T>` for lists); Stripe.net
(`SessionListOptions : ListOptions` shared base) and OpenAI .NET (`*CollectionOptions`)
as ecosystem precedent.

**Decision (maintainer, sealed):** per-operation generated options records deriving from a
shared `ListOptions` base that carries only the cursor-pagination trio; a fail-closed
generator profile-detection wall derives an operation from the base only on an exact wire
match (otherwise flat options); the base is the typed seam the M3 paginator will consume.
`*Options` = call shaping, `*Request` = wire body.

## Q77: How is the string-typed `limit` exposed?

**Found:** the wire types `limit` as a string; `uint` was weighed and rejected — FDG bans
unsigned types in public APIs (CLS compliance), the ecosystem has zero precedent
(`Take(int)`, Stripe `long?`, OpenAI `int`), and it does not even buy the invariant
(zero stays representable, so a guard is needed either way).

**Decision (maintainer, sealed):** public `int?`, invariant-culture conversion at the
route boundary, non-positive values refused with `ArgumentException`.

## Q78: How does the API express parentID's root-only sentinel?

**Found:** the wire has three states — omitted (all sessions), `parentID=<id>` (children),
and the literal string `"null"` (root sessions only). The schema shape is
self-identifying: `anyOf` of a `^ses`-patterned string and a single-value `"null"` enum.

**Decision (maintainer, sealed):** a hand-written public spine type
`SessionParentFilter` (`RootOnly` singleton, `Of(id)` factory) carried by the options
record; the binder recognizes the wire shape mechanically, never the parameter name
(ADR-0008 stays intact); invalid combinations are unrepresentable at compile time.

## Q79: What shape does the first request body take?

**Decision (maintainer, sealed):** `session.create`'s inline all-optional body binds into
a generated `SessionCreateRequest`; the operation parameter is optional —
`CreateSessionAsync()` sends an empty JSON body. The `{Subject}{Verb}Request` pattern is
the mechanical rule for future bodies.

# Session 20 — 2026-08-14: M2 first breadth batch execution

The batch executed per the sealed plan: binder walls opened red-test-first (query options
with the profile-detection wall, request bodies, cursor-list envelopes, merged client
families), the pipeline grew its JSON body path, the naming and fail-closed walls batches
(#22, #21) and the F07 carrier converters (#19) landed, and the four operations were
demonstrated live against `opencode2 serve` v0.0.0-next-17403 (create → list → get →
messages, all typed 200s, wire cursor round-tripped). This entry records the decisions
sealed during execution.

## Q80: What are the concrete names of the query surface's supporting types?

**How researched:** batched to the maintainer with previews once the binder was about to
encode names; Stripe (`SessionListOptions`) and the sealed `{Subject}{Verb}Request` rule as
anchors.

**Decision (maintainer, sealed):** options records follow `{Group}{Verb}Options`
(`SessionListOptions`, `MessageListOptions`), mirroring the request-model rule so one
mechanical family covers both; the shared order enum is the hand-written spine
`ListOrder { Ascending, Descending }` (request-only closed enum — ADR-0009 tolerance does
not apply); the wire cursor surfaces through one hand-written `ListCursor` record on every
list envelope, giving the M3 paginator a single cursor seam. The binder validates the wire
shapes (`asc`/`desc` exactly; `{previous?, next?}` exactly) and fails closed otherwise.

## Q81: How is upstream's InvalidRequestError1 duplicate resolved?

**Found:** the plan's recon framed it as a naming dup; execution showed it byte-identical to
`InvalidRequestError` *including the `_tag`*, which trips the per-status duplicate-tag wall
and would poison `OpenCodeError` tag dispatch. Two candidate mechanisms were priced: a
mechanical structural dedup (which needed a novel recorded-output channel to satisfy the
maintainer's drift-visibility requirement) and a curated alias.

**Decision (maintainer, sealed):** a `schemaAliases` curation section — one row declaring
the duplicate a spelling of its target, validated fail-closed for deep structural identity
and applied as a pure document transform before binding. Drift stays loud through existing
machinery: a deleted source or target orphans the row, a dereferenced source goes dormant
against the profile, and any structural divergence (the tag included) breaks the identity
wall. The binder's own duplicate-tag refusals stay intact for anomalies without an alias.

## Q82: How do the two options-like parameters coexist on generated methods?

**Decision (maintainer):** the query record binds to `options` and `OpenCodeRequestOptions`
binds to `requestOptions` on every generated method — the type's own name decides, matching
Stripe's idiom; the pre-release rename of the existing surface rode the PublicApi baseline
review.

## Execution notes

- The verb rules (sealed decision 5) concretized as: only the final identifier segment can
  be a verb (`create`/`get`/`list` — the C18 structural fix); empty subjects fall back to
  the group, pluralized for list operations under a naive fail-closed rule; response type
  names fold non-Get verbs; client-placed route members mirror their method names while
  root members keep the bare-verb shape (`Health.Get`).
- The P2 single-pass envelope DTOs cut `GetMessageAsync` from 67.4 μs to 56.2 μs with flat
  allocations; `ListMessagesAsync` baselines at 58.1 μs / 28.24 KB.
- The contract matrix caught a real defect before any consumer could: the error-path
  constructor pushed its forgiven null through the list payload's defensive-copy init
  accessor; the copy now passes the forgiven null through uncopied behind the guard.

## Q83: Do operation inputs stay split across *Request and *Options?

**How researched:** maintainer review question (AWS-style Request/Response symmetry);
ecosystem survey — AWS and Google Cloud use a uniform `{Operation}Request` for every
operation regardless of wire placement (query vs body is a marshalling detail), Stripe and
OpenAI use a uniform `*Options` (POST bodies included), Azure's options bag carries only
optional inputs; **no surveyed SDK splits the suffix by verb** as Q76's dichotomy did.

**Found:** the sealed dichotomy had married halves of two different families, and the
verb→suffix mapping is unstable — a future query-carrying POST would demand two input bags
per call where the uniform-Request shape carries one.

**Decision (maintainer, sealed):** uniform `*Request`, revising Q76's naming half. Query
records rename (`SessionListRequest`, `MessageListRequest`, base `ListRequest`); the body
models already carry the name; the method and route-builder parameter becomes `request`.
The profile-detection wall and the M3 paginator seam carry over under the new name. An
operation declaring both a body and query parameters would mechanically derive one type
name twice and fail the existing collision wall — deliberate, until a merged-Request
design (AWS marshalling style) is sealed. Execution: next session
(`agents/handover-prompts/HANDOFF-2026-08-14-2.md`).

## Q84: How does the shipped SDK lay out?

**Decision (maintainer, sealed):** vertical feature slices with flat public namespaces —
client families as folders (`Sessions/`, `Health/`), the pagination spine under
`Pagination/`, `Models/` and `Internal/` unchanged, the root client and response/exception
spine at the project root. Public namespaces stay `OpenCode.Sdk` and `OpenCode.Sdk.Models`:
a namespace is API surface, folders are placement (Stripe/Azure precedent). Test projects
mirror the layout of the project under test. IDE0130's folder-matches-namespace rule is
arbitrated for the SDK's public folders per the standing per-rule pattern. Canonical
wording: `engineering/coding-style.md` §4. Execution: next session.

## Q85: When does the Extensions package rise?

**Decision (maintainer, sealed):** in parallel with M2 breadth instead of waiting for M6 —
an `AddOpenCode` bring-up (overloads mirroring the three client constructors, root plus
sub-client registrations so a consumer can resolve `SessionsClient` directly) lands as its
own batch; the ROADMAP moves "Extensions DI breadth" out of M6 accordingly.

## Review decisions closed with the batch

- **#20 (password semantics)** sealed as recommended and landed (`2be2d0f`): `null` = unset
  with the environment fallback; empty or whitespace refused with `ArgumentException` — an
  explicitly blank credential has no upstream meaning; `""` = explicit no-auth stays an
  additive door if upstream ever ships passwordless serve.
- **#25 (DynamicProxyGenAssembly2 IVT)** closed keep: the friend grant is the standard
  NSubstitute mechanism over internal seams and a recorded solved-once decision; internals
  are not a security boundary. The recorded alternative (hand-written fake replacing the
  grant) applies only if encapsulation purity is ever preferred.

# Session 21 — 2026-08-14: Alignment batch execution

The three sealed alignment decisions (Q83–Q85) executed on the PR #26 branch: the uniform
`*Request` rename with its body-plus-query double-derivation refusal pin, the feature-slice
layout migration with the writer's fail-closed family-folder allowlist, and the Extensions
bring-up. No sealed decision was reopened; one forward-looking note is recorded.

## Note: generator-emitted DI registrations

`AddOpenCode`'s sub-client registrations are hand-written — one factory line per client
family. Emitting these registrations from the generator becomes worthwhile when client
families multiply across breadth batches: the registration list is the same mechanical
projection the client emitters already own. This is a trigger, not a commitment.

# Session 22 — 2026-08-14: Extensions grill, upstream re-research, location census

The maintainer grilled the Q85 first cut (constructor-mirror DI shape, BYO-HttpClient
overload, missing IHttpClientFactory, options mutability, env-fallback double-headedness)
and demanded upstream verification instead of vision-doc relay. Findings re-derived at the
pinned commit `a6a712a` directly; canonical detail in research doc 15 (§5a, §6). The
`external/opencode` submodule pointer was found stranded on the 1.x line and moved to the
spec pin with a lockstep rule in `spec/SNAPSHOT.md`.

## Q87: Is `x-opencode-directory` dead on v2?

**Found:** no — evolved. The server's location middleware resolves dual-channel:
`location[…]` deepObject query (61 ops, spec-visible, used exclusively by the first-party
generated client) OR ambient `x-opencode-directory`/`x-opencode-workspace` headers
(spec-invisible, middleware-level), precedence query > header. The vision spec's
header-only `Directory` story is obsolete; its §6 was corrected in place.

## Q88: Can the merged-Request need be foreseen instead of wall-triggered?

**Found:** yes — censusable today: 15 ops mix body + query, and in all 15 the only query
parameter is `location`; `v2.session.list` is the single flat-field exception. The merged
marshalling question and the location question are one design.

**Decision (maintainer, sealed):** the location + merged-Request input design is done
proactively in a short design session that opens M3 planning — not deferred until the
collision wall fires. Census and mechanism notes live in doc 15 §5a/§6.

## Q89: Where do v2 endpoint and credentials actually come from?

**Found:** no URL environment variable exists; the first-party discovery contract is the
registration file (doc 15 §6). Auth is optional (`ServerAuth.required` = configured,
non-empty password). The username is `opencode` at the pin (the pinned server hardcodes
it); `--username`/`OPENCODE_SERVER_USERNAME` is upstream direction — docs and the desktop
sidecar — not pinned-server behavior, so the `Username` option is the gateway and
forward-compatibility seam. Client-side
password-from-environment resolution lives in the CLI consumer
(`OPENCODE_PASSWORD` → legacy `OPENCODE_SERVER_PASSWORD`), not in the client library.
Feeds the pending Q86 batch (options shape, env-fallback ownership, `Username` option).

## Q90: How do client construction, options, and DI align with .NET conventions? (the in-session "Q86" batch)

**How researched:** the maintainer's grill of the Q85 first cut plus the Q87–Q89 upstream
findings; ecosystem verification of options immutability (init-only binds via the
reflection binder; the .NET 8+ config source generator's required/init support is the
buggy edge — dotnet/runtime #95006/#101984/#90974 — so `required` stays out).

**Decisions (maintainer, sealed):**

- **Construction:** `OpenCodeClient(OpenCodeClientOptions)` + the caller-owned-HttpClient
  overload; the Uri constructors retire — the endpoint has one home and the
  endpoint-must-stay-unset guard disappears. One way to build a client.
- **Options:** the .NET convention holds — a settable class with the `Action<>` configure
  pattern — and immutability is expressed at consumption: the public read-only
  `IOpenCodeClientOptions` view, snapshotted by the pipeline at construction (pinned by
  test: post-construction mutation never reaches a built client).
- **Credentials:** `Username` joins the options (upstream's `--username` /
  `OPENCODE_SERVER_USERNAME`, default `opencode`; the pipeline's hardcode retired);
  `Password` stays optional — null sends anonymous requests, matching v2's optional auth;
  blank refusal stays. The environment fallback left the SDK (upstream layering: the
  consumer resolves env; the CLI's own chain is `OPENCODE_PASSWORD` →
  `OPENCODE_SERVER_PASSWORD`) — this revises the env half of the #20 decision on Q89
  evidence; the blank-refusal half stands.
- **Extensions:** rebuilt on `IHttpClientFactory` — `AddOpenCode(Action<...>)` +
  AOT-annotated `AddOpenCode(IConfiguration)` bind options, register a transient typed
  client over a named factory client, register sub-clients explicitly, and return the
  `IHttpClientBuilder` so consumer handlers/resilience/telemetry chain on without SDK
  middleware. The BYO-HttpClient registration overload is gone (a captured instance
  defeats factory rotation; no surveyed companion package ships one).
- **Transport:** the self-created path uses `SocketsHttpHandler` with
  `PooledConnectionLifetime` on modern TFMs (vision §7.5's promise, implemented); the
  net472 `ServicePointManager` half stays an M3 item.
- The sandbox is the standing DI showcase (Generic Host, builder-chained consumer
  handler, direct sub-client resolution).

# Session 23 — 2026-08-15: Q91 BYO-transport seal

## Q91: Does the caller-owned `(HttpClient, options)` constructor stay public?

**How researched:** seven-agent primary-source survey (2026-08-14, versions pinned at
release tags) across Azure.Core/System.ClientModel, OpenAI, AWS v4, Stripe.net, Octokit,
the typed-client ecosystem (Refit, Kiota, Grpc, Elastic, MCP), and the
BCL/IHttpClientFactory layer — census, findings, and sources in research doc 16; upstream
v2 JS client read at the pin (`packages/client`: public `fetch` option, SDK-wins header
precedence encoded in the generated client).

**Found:** no surveyed SDK gates transport injection behind a DI companion (AWS v4
removed the knob from its DI options type); every SDK resolves header conflicts by the
BCL's silent request-wins merge with zero guards, and every surveyed anonymous mode leaks
a caller default `Authorization`; the BCL cannot express "anonymous by omission" over a
caller client, and the same leak exists on the factory path via `ConfigureHttpClient` —
so the pipeline guard is required regardless of the constructor's visibility; the stock
typed-client pattern hard-requires a public `(HttpClient, …)` constructor. The
internalize+IVT leaning and a handler-seam-on-options alternative were both weighed and
rejected (doc 16 §4) — the latter on the Q90 bindable/snapshot options contract.

**Decision (maintainer, sealed):** the constructor stays public. Anonymous mode fails
closed — `Password == null` while the injected client's `DefaultRequestHeaders` carry
`Authorization` refuses at construction and before every send (construction-only would
miss legal post-construction mutation). With `Password` set, the SDK's per-request
`Authorization` deterministically wins (BCL request-wins merge); the precedence contract
is documented on the constructor and options. Standalone handler/proxy/TLS composition
rides the BYO client; concrete convenience knobs remain an additive future. Executes as
review blocker #1's fix in this batch.

# Session 24 — 2026-08-15: Q92 construction simplification seal, M3 plan agreed

PR #35 (the #34 contract-test consolidation) merged after a three-dimension adversarially
verified branch review (semantics and multi-TFM clean; three style misses fixed on the
branch). The M3 plan was agreed and canonicalized (`superpowers/plans/2026-08-15-m3-plan.md`);
the 2026-08-15 handover deleted per its own condition.

## Q92: Does the SDK keep the ecosystem-shaped transport surface into production?

**How researched:** maintainer-driven premise change on the Q90/Q91 surface — go to
production as simple as possible; do not invest in transport/pipelining extensibility
before a concrete need — assessed against doc 16's census, the BCL's HttpClient guidance
(long-lived singleton client + `PooledConnectionLifetime` is the documented alternative to
`IHttpClientFactory`), and the consumer reality: a local-first daemon discovered by
registration file, pre-1.0, zero consumers, packing still blocked.

**Found:** the factory/rotation apparatus solves cloud-endpoint stale-DNS; the owned
transport's `PooledConnectionLifetime` (Q90) already covers the remote case on modern
TFMs, while net472 (no `SocketsHttpHandler`) needs the `ServicePointManager`
connection-lease hardening to make a long-lived client correct. Doc 16 §4's two grounds
for rejecting internalize+IVT dissolve under the new premise: stock
`AddHttpClient<OpenCodeClient>()` support is withdrawn, and the factory path whose guard
justified the public constructor no longer exists. Removal is the reversible position —
re-adding a public transport constructor is additive; removing one post-GA is a breaking
major. Our own contract tests and benchmarks are the remaining transport-injection
consumers.

**Decision (maintainer, sealed):** simplicity-first construction into production.
`OpenCodeClient(OpenCodeClientOptions)` is the only public construction path; the
`(HttpClient, options)` constructor internalizes as IVT test-only surface. Q91's
public-constructor half is reversed on the changed premise (doc 16's evidence stands as
record); its guard machinery — the anonymous-mode `Authorization` refusal and the injected
`BaseAddress` refusal — deletes with the doors it defended. The Q90 password semantics
(`null` = anonymous, blank refused) stand. `OpenCode.Sdk.Extensions` drops
`IHttpClientFactory` and the `Microsoft.Extensions.Http` dependency: `AddOpenCode`
registers one singleton root client over the owned transport plus sub-clients resolved
from that same instance, returning `IServiceCollection`. #31 resolves by construction
(singletons end-to-end: no transient-disposable tracking, no scope poisoning, no split
pipelines); its roster contract test survives as the one live remainder. Accepted and
recorded: no consumer composition seam (proxy/mTLS/resilience/telemetry handlers) before
M6's hook design — the common proxy case rides the ambient `HttpClient.DefaultProxy`; the
net472 connection-lease item promotes to a GA gate; the mocking constructor is the
consumer substitution point for testing. Canonical record: ADR-0010. Executes as its own
PR opening the M3 runway.

## Review decisions sealed with this session

- **#28 (curation mutual-exclusion)** sealed as recommended: an operation-level
  mutually-exclusive-query curation row, fail-closed binder validation, route-boundary
  `ArgumentException` before any request, with a contract test proving no send;
  session-list's legitimate order+cursor composition untouched.
- **#32 (EscapeDataString TFM divergence)** sealed as uniform route-boundary refusal:
  lone surrogates and oversize inputs refuse with a typed `ArgumentException` on every
  TFM before escaping.
- **#33 (carrier hand-construction)** sealed as constructor refusal: the payload must
  carry the union's marker property agreeing with the tag — never fires on the wire read
  path, refuses the silent marker-drop hand-construction; `Write` stays payload-only
  verbatim replay (ADR-0009 intact). Executes inside #23's converter rewrite.
- **#31 (DI lifetime shape)** superseded by Q92 — closes with the reshape PR; the roster
  contract test carries over into it.

# Session 25 — 2026-08-15: M3 Arc 2 — location + merged-Request design seals

The short design session sealed Q88's two questions on the recommendations; the
body+query admission stop condition lifts. Census inputs: doc 15 §5a/§6.

## Q93: How does one uniform `*Request` carry body-bound and query-bound properties?

**Decision (maintainer, sealed):** a binder-owned per-property placement map (AWS
marshalling style). The operation plan records each request property's wire placement
(body, query, path); emitters stay name-blind and route composition reads placements
mechanically; the deliberate body+query double-derivation refusal retires per-operation
as the map admits it. Upstream parity: the v2 first-party generated client emits one
uniform `{Op}Input` per operation.

## Q94: How does the SDK render the dual-channel location mechanism?

**Decision (maintainer, sealed):** both channels, layered by the server's own
precedence. The location-carrying operations get a generated `Location` property on
their request records (the spec-visible deepObject channel the first-party client uses
exclusively; `location[directory]=…&location[workspace]=…` marshalling implemented once
in route composition), and `OpenCodeClientOptions` gains an ambient default riding the
spec-invisible `x-opencode-directory`/`x-opencode-workspace` header channel. An explicit
per-request `Location` wins over the ambient default because the server resolves query
before header — the SDK performs no client-side merge. `v2.session.list`'s flat location
fields bind as ordinary query properties with no special case; the fail-closed walls
stay.

# Session 26 — 2026-08-16: Q95 location-channel tour, measured

The maintainer challenged the ambient location header mid-batch ("deprecate olmuştu,
şemada da yok artık"). The claim was investigated against upstream rather than argued,
the batch's encoding defect was found and fixed as a result, and the ambient channel's
future became an M5 decision (#37).

## Q95: Is the `x-opencode-directory` header deprecated, and how does location actually resolve?

**How researched:** three parallel primary-source sweeps over `external/opencode` at the
spec pin (`a6a712a`) — the generated JS SDK, the server/core internals, and every
consumer — plus live probes against `opencode2 serve` v0.0.0-next-17403 started in a
directory distinct from every probe target.

**Found — not deprecated, and never removed from the schema:**

- The submodule sits exactly on the spec pin, and upstream `v2` is 164 commits ahead of
  it; at that live HEAD `packages/server/src/location.ts:31,34` still reads both headers,
  still `decodeURIComponent`s the directory one, and **no commit has touched the file
  since the pin**. No deprecation marker exists anywhere in `packages/server` or
  `packages/core`.
- The header was never expressible in the document, so it was never scrubbed from it. The
  spec is `OpenApi.fromApi(ClientApi)` (`packages/protocol/script/generate-openapi.ts`), a
  16-line script with no filtering, and that pipeline derives parameters only from declared
  endpoint schemas. Headers *are* expressible there, but no protocol endpoint declares
  `headers:` at all; the location headers are read imperatively inside middleware, which
  contributes nothing to the parameter surface. **Control case: `Authorization` is invisible
  the same way** — the server genuinely requires Basic auth while the generated document
  carries empty `securitySchemes`.
- Git history rules out replacement: `location.ts` was created in `56a37c3640` with the
  header *and* query branches already present, so the query channel was additive from day
  one. `specs/v2/schema-changelog.md` frames it as preserving header routing for
  compatibility.

**Found — the channels are not interchangeable:**

- `LocationMiddleware` is attached **per group** (`packages/protocol/src/api.ts:150-180`)
  and applies to every endpoint in that group regardless of whether it declares the query
  parameter. So the header reaches operations the query channel cannot (`project.list`,
  `permission.saved.*`), and is inert on groups without it (health, server, message,
  event, debug, migration) and on session-scoped endpoints, which resolve location from
  the session DB row and ignore both channels.
- **Precedence is per field, not per location object** (measured): query beats header beats
  `process.cwd()`, but `directory` and `workspaceID` resolve independently and the code uses
  `||`, so an empty query value falls through to the header. Consequently an ambient
  `{directory, workspaceID}` plus a per-request query carrying only `directory` resolves to
  the per-request directory **and the ambient workspace** — a per-request location does not
  wholesale-replace the ambient one.
- No first-party client *library* offers an ambient location: `ClientOptions` is
  `{baseUrl, fetch, headers}` and every UI (app/web, TUI, CLI) threads location per call
  through the query channel. Only `packages/drive`, a single-directory test harness, pins an
  ambient header — the same shape this SDK ships.

**Found — a defect in this batch:** the ambient directory header rode the wire raw. Measured
against the live server, a directory literally named `loc%20test` returns **HTTP 500 with an
empty body** when sent raw (the server decodes `%20` into a space and resolves a directory
that does not exist) and **200 with the correct location** when percent-encoded. The
workspace header is *not* decoded server-side, so the escaping is asymmetric by contract.

**Decisions (maintainer, sealed):** the ambient channel stays for now (option A) with its
documentation corrected to state per-field precedence and the coverage boundary; the
directory header is percent-encoded and the workspace header stays verbatim, mirroring the
server. Whether to drop the ambient channel entirely (option B — "convenience does not
justify supporting a channel outside the public contract"; the MCP server will set location
explicitly anyway) or fold it into the query channel (option C) is anchored at **M5 as
issue #37**, decision-first because packaging unblocking freezes the public surface.

# Session 27 — 2026-08-16: Q96 streaming endpoints generate

Arc 3 opened with a recon pass whose numbers contradicted an ADR clause. The maintainer
raised the challenge — "ADRs are not scripture, and the hand-wired rule was written while
we were pinned to 1.18.15, where the contract expressed SSE weakly or not at all; more
hand-wired code means more fail-open, because we would be tracking changes outside the
schema without knowing" — and the slice stopped per the deviation protocol's Level 3.

## Q96: Are the streaming endpoints hand-wired or generated?

**How researched:** the pinned spec's two `text/event-stream` operations read directly;
the upstream code generator (`packages/httpapi-codegen`) and the client it produces read
at the pin; model-closure sizes computed over the component graph; ADR-0008's own
reasoning re-read against its subject.

**Found — the contract expresses SSE in full.** Both operations declare
`text/event-stream` with a frame schema `{id, event, data}`, `data` typed through
`contentSchema` + `contentMediaType: application/json`, and an `x-effect-stream`
annotation carrying the encoding and a failure-cause schema. Nothing about this is thin:
it is the same schema-driven material every other operation binds from.

**Found — upstream generates its own streaming operations.** `httpapi-codegen/src/index.ts:895`
emits `(args): AsyncIterable<XOutput> => sse<XOutput>(descriptor, requestOptions)`, while
the `sse<A>()` reader — status wall, content-type wall, CRLF normalization, `\n\n` frame
boundaries, `data:` collection, a maximum-frame guard, `JSON.parse` — is emitted once as
runtime scaffolding. That is precisely the split ADR-0008 prescribes for the one-shot
surface: behavior in the core, endpoints as one-line delegations.

**Found — ADR-0008's own thesis argued against its own clause.** Its central reasoning is
that hand-written op methods "sit outside CI regen-verify and go silently stale as upstream
moves," while generated ones "turn every spec drift into a loud diff or a broken build."
That argument is indifferent to whether a response is one-shot or streamed. The clause
naming stream-endpoint wiring as hand-written was written the same day as the v2 retarget
(ADR-0005), whose own text schedules the retarget as a later task — so the clause predates
sight of the contract it now governs.

**Found — the economics favor generation.** The payload models are generated either way:
`SessionLogItem` closes over 58 schemas new to us, `V2Event` over 125 (87 union branches,
43% of the spec's component graph). What remains in question is only the short method that
names a route, a payload type, and a declared-status map — the very piece most likely to
drift, and nearly free to emit once everything around it is emitted.

**Decision (maintainer, sealed):** streaming endpoints generate. ADR-0008 is corrected in
place: the SSE **engine** stays hand-written identity core (transport send, status and
content-type walls, the frame reader, cancellation, disposal), while stream **endpoints**
emit as one-line delegations into it. Streams yield `IAsyncEnumerable<T>` instead of a
response envelope, so `NoThrow` has no channel to answer on and is refused rather than
ignored. Exclusion narrows to transports HTTP cannot carry: `pty.connect` upgrades to
WebSocket, leaving HTTP after the handshake, and stays excluded and fingerprint-pinned.

## The two stream endpoints, and why there are two

`v2.event.subscribe` (`/api/event`) is the **live global bus**: 87 branches spanning the
whole daemon — catalog and agent refreshes, integration and session lifecycle, execution
start/success/failure, inbox delivery. It declares no parameters at all, not even location:
one subscription sees everything, and consumers fan out client-side. Nothing is replayable,
which is why it carries no resume parameter.

`v2.session.log` (`/api/experimental/session/{sessionID}/log`) is the **durable per-session
log**: two branches, `Session.Event.Durable` and `EventLog.Synced`, with `after` to resume
from a position and `follow` to keep streaming. This is the replacement for the v1 durable
pair, and its `after` — not the SSE `id:` line, which the first-party reader discards — is
the resume mechanism.

## Q97: Do streaming operations carry per-call request options?

**How researched:** raised while the streaming pipeline path refused `NoThrow` at run time.
`OpenCodeRequestOptions` carries exactly one member, `ErrorBehavior`, and a stream has no
envelope for an error to ride, so the type had nothing left to say on that surface.

**Decision (maintainer, sealed):** streaming operations take no per-call options. The
refusal was preventing at run time what the compiler can prevent outright, against the
same instinct that made `SessionParentFilter` unrepresentable-when-invalid (Q78). An empty
stream-options type was weighed and rejected as speculative — it would carry nothing today.
Recorded on ADR-0007 with its reversal trigger: M6's retry/telemetry/hooks work, if it
gives a stream call something real to carry.

**Accepted cost:** the streaming methods' signatures diverge from the one-shot surface,
which is honest rather than hidden — a stream's contract genuinely differs in that it
always throws.

## Q98: Is the SSE `event` field information, and what does the contract use it for?

**How researched:** raised in code review — the frame reader discarded every field except
`data`, while the pinned spec declares `id`, `event` and `data` all `required` on both
stream responses. Read Effect's SSE encoder and parser (`effect/unstable/encoding/Sse.ts`),
both server handlers, and upstream's own generated client.

**Found — `required` describes the decoded envelope, not the wire.** The parser fills a
missing `event:` line with `"message"` before the value is ever seen, so the decoded shape
always has one. The encoder is the other half of the same coin: it writes `event:` **only
when the name is not `"message"`**, and `id:` only when one is defined. An ordinary payload
frame therefore carries neither line.

**Found — that makes `event` a pure signal channel.** Its presence means the frame is not a
payload. The one signal this API declares is `x-effect-stream.failureEvent:
effect/httpapi/stream/failure`, sitting beside `causeSchema` and `errorSchema`. The reason
is structural: once a 200 and its headers are on the wire, HTTP has no way left to say the
operation failed, so Effect encodes the failed cause as an in-band frame with a reserved
name — the SSE analogue of a trailer.

**Found — the two endpoints do not behave alike, despite identical declarations.**
`v2.session.log` is a `.handle(...)` returning a `Stream`, so it runs through the real
`HttpApiSchema.StreamSse` encoder and can emit the failure frame. `v2.event.subscribe` is
`handleRaw`: it writes `data: ${json}\n\n` by hand and keeps the connection alive with
`: heartbeat\n\n` comment lines every 15 seconds, so it never emits `event:` at all and its
failures simply close the connection.

**Found — upstream's generated client has the hole we had.** `httpapi-codegen`'s reader
keeps only `data:` lines and discards the rest, so a failure frame reaches it as an
ordinary payload. Effect's own parser keeps the name. We follow the parser, not the codegen
client.

**Decision (maintainer, sealed):** frames dispatch as `ServerSentEvent(Name, Data)`. The
adapter carries the operation's declared `failureEvent`; an unnamed frame is the only shape
that carries a payload, a failure-named frame throws, and any other name is refused rather
than parsed. Without this a server-side stream failure could arrive as a benign event
through ADR-0009's unknown-variant carriers — the silent fallback the engineering
conventions ban.

**Accepted cost:** refusing an undeclared event name is stricter than upstream, which would
parse such a frame as a payload. Fail-closed is the house position, and a new name should
surface loudly rather than be mis-read.

## Q99: What should a body that ends in the middle of an event mean?

**How researched:** raised in code review — a stream cut mid-line folded its partial line
into the pending frame and dispatched it, so the truncated JSON then failed as a "malformed
frame payload" and blamed the server for a dropped connection.

**Found — both references dispatch it too.** WHATWG says an incomplete event is not
dispatched, but upstream's client appends `\n\n` to whatever remains at EOF and dispatches
it, reaching the same wrong answer we did.

**Found — the browser rationale does not transfer.** `EventSource` discards silently
because it reconnects and re-reads; this SDK deliberately does not auto-reconnect (research
doc 02), so a discarded remainder is simply lost data the consumer never hears about.

**Decision (maintainer, sealed):** a body ending mid-line throws
`OpenCodeTransportException` naming the truncation. A body ending after a complete line
still dispatches its pending frame — that is a whole event missing only its blank line.

**Accepted cost:** a deliberate divergence from both WHATWG and upstream, taken because
neither's reasoning survives the no-reconnect posture, and because reporting a cut
connection as a malformed payload misdirects every consumer who reads the message.

# Session 28 — 2026-08-17: post-Arc 3a review, factual verification, and policy seals

The 25-commit Arc 3a range (`f6a569a..b19e32d`) received its first independent review before Arc 3b could build on
it. Eighteen fresh-context reviewers covered the streaming runtime, generator slices, generated
surface, both test projects, shared infrastructure, concurrency, performance, TFM/AOT behavior,
and the handoff's factual verification prompt. Their reports were cross-fed at generated/tooling
boundaries, then every surviving class was checked against live source, executable probes, or a
primary input before issue filing. Fifteen trigger-scoped issues (`#39`–`#53`) survived; existing
`#23`, `#24`, `#27`, and `#30` gained same-owner riders instead of duplicates.

## Q100: Did the handoff's factual claims survive primary-source verification?

**How researched:** exhaustive `jq` walks over the pinned spec; pinned Effect encoder/parser,
server handlers, generated client, package assets and per-TFM binaries; before/after Git
worktrees for ADR-0011; and an isolated exact `opencode2` v0.0.0-next-17403 server with its own
home, database, and port.

**Found:** claims 1–11 held. The decisive counts are 39/40 durable branches shared directly
with the 87-branch live union, 41 numeric singleton enums all declared `number`, 12 exact
object-or-array empty-struct spellings, and four all-string refinement unions. The two SSE
handlers, Effect's conditional `event:`/`id:` emission, generated-client field loss, direct
dependency split, interface converter support, and downlevel Polyfill allocation also held.

Claim 12 was too broad: the suite passed and one API baseline was identical across the three
modern test TFMs, but the interface conversion intentionally changed that reviewed baseline,
the reflected type graph, and record `ToString()` behavior. Covered wire round trips stayed
stable; “no runtime behavior changed” did not.

Claim 13 was false on its sequence half: default-server durable commits advance the watermark
while payload persistence remains disabled. This is configured behavior, not an inert writer or
unexplained build gap; research doc 02 carries the source trace and exact live observation.

## Q101: Which review findings were real enough to schedule?

**Found:** executable runtime probes reproduced stream-side untouched-token cancellation,
enumeration after disposal, a buffered frame after cancellation, malformed UTF-8 becoming
typed replacement-character data on both paths, a one-shot body outliving `HttpClient.Timeout`,
JSON success under `text/plain`, automatic redirect following, named special-number rejection,
null elements inside non-null collections, and unenforced fixed/closed-object constraints.
Generator traces separately proved incomplete stream-profile walls and first-appearance walls
across ingestion, binding, naming, curation, and writer recovery. The reported clean-build
analyzer explosion was discarded: concurrent generation had exposed transient unformatted
output, and a clean no-incremental five-TFM build completed with zero warnings and errors.

**Decision:** immediate M3 correctness work is executable under `#39`–`#42` and `#44`–`#49`;
`#43` remains the decision-first Arc 5 transport cluster. The broader fail-closed and release
gates are trigger-anchored in `#51`–`#53`. Performance findings remain on `#23`/`#29`; no
hot-path optimization moves before Arc 6.

## Q102: How do the two public names the projection cannot express enter the API?

**Found:** `after` is projected as a string, but upstream decodes `NumberFromString` into the
non-negative integer `Event.Seq`; Q31's promised parameter override had never been implemented.
The mechanical operation-name policy reads `v2.event.subscribe` as HTTP GET plus subject
“Subscribe”, yielding `Events.GetSubscribeAsync`; a global verb-list addition instead yields
`Events.SubscribeEventAsync`.

**Decision (maintainer, sealed):** `SessionLogRequest.After` is `long?`, accepts zero, and
refuses negatives before sending through generic reason-bearing parameter curation (`#40`).
The live bus is `Events.SubscribeAsync`, supplied by generic reason-bearing operation-name
curation with fail-closed identifier/collision validation (`#44`). Machinery never branches on
either operation ID.

## Q103: How strict are fixed values and additive fields on known models?

**Found:** fixed literals and object openness survive ingestion but disappear during model
emission. `healthy:false`, arbitrary durable versions, contradictory direct concrete markers,
and extra fields on a closed known object all deserialize today. The mechanisms have different
version-skew consequences: a wrong fixed value contradicts the represented variant, while an
unknown optional field is the normal shape of an additive newer server.

**Decision (maintainer, sealed):** every fixed boolean, number, or string emits as a
constant/get-only property and validates its wire value through one name-blind literal rule
(`#45`; ADR-0004). Known objects deliberately skip additive unmapped fields even when the pin
closes them; required members, fixed values, represented types, and nullability remain strict.
Pure dictionaries keep their value schema, and a hybrid named object with typed additional
properties fails binding until represented without loss (`#46`; ADR-0012).

## Q104: What proves breadth when real stream frames are sparse?

**Found:** the durable runtime corpus covers two of 40 durable branches, not 40, and several
older “known” message fixtures use IDs outside the pinned `^msg_` contract. A generated JSON
fixture factory would add a maintained test-only schema interpreter, while 40/87 hand-authored
payloads would multiply the unobserved wrong-at-birth risk.

**Decision (maintainer, sealed):** runtime fixtures stay representative, small, schema-valid,
and increasingly observational. Mechanical tests own exhaustive converter tags, source-generated
registry membership, and bind → emit → compile → deserialize plural-interface guarantees. Real
frames join the corpus as they become available; documentation states the actual runtime count
(`#49`, testing style).

## Q105: Did the performance and hosted-assurance guardrails hold?

**Found:** an unchanged `MemoryDiagnoser` run reproduced all six M3 allocation baselines exactly:
25.92 KB, 26.67 KB, 2.08 KB, 17.37 KB, 321.54 KB, and 177.69 KB. GitHub Actions had failed to
start after `683341a` because of account spending controls, leaving the Arc 3a range without
hosted Windows/net472 evidence. After the maintainer restored budget, rerun `32008926694`
attempt 2 completed all three Windows, macOS, and Linux jobs successfully against source SHA
`b19e32d`.

**Decision:** the interim allocation baselines remain hard guards through Arcs 3b–5. Hosted CI
execution is restored; branch-protection policy remains tracked separately by `#50`.

# Session 29 — 2026-08-17: protocol authority and runtime boundary reset

## Q106: Where does SDK runtime validation stop?

**How researched:** four fresh-context read-only audits traced runtime serialization, generator
mapping, tests/canonicals, and performance/AOT from clean `050b4f8`; a fifth pass compared the
pinned upstream Promise client, Effect client, server encoder, and Effect schema runtime at the
same `a6a712a` pin. Local package decompilation supplied controls: AWS SDK for .NET 4.0.100.8 and
Azure Search Documents 12.0.0 admit some nested nulls, while Google.Protobuf 3.34.1 explicitly
refuses them. The current selected output contains 94 collection properties and 109
wire-null-rejecting attributes; the deep-assistant benchmark fixture reaches repeated empty
dictionary construction and deserialize-then-copy paths.

**Found:** upstream has no single answer. Its Promise client parses and casts representable JSON;
its Effect client replays schema validation; normal server encoders prevent many contradictions
from reaching either. The .NET SDK had mixed those postures: it distinguished omission from null,
rescanned page elements, normalized optional collections to empty, copied and wrapped every
collection, and planned validation for all fixed literals. Those checks are unnecessary when the
wire value already materializes in the declared C# shape, and `IReadOnly*` plus a copied outer
container still does not prove a recursively immutable object graph.

**Decision (maintainer, sealed):** runtime validates transport/framing, JSON materialization,
required .NET shape, and union dispatch; it does not revalidate representable server values against
OpenAPI (ADR-0014). `required` follows schema presence; nullable C# means optional or
schema-nullable; required-nullable values write explicit null; optional collections remain null
rather than becoming empty. Generated collections are shallow init-only `IReadOnly*` properties
with caller-owned mutation. Collection children are annotation-only, non-discriminator literals
remain ordinary properties, declared 204 bodies are ignored, and optional explicit null collapses
with absence. Buffered JSON charset/BOM handling remains delegated to `HttpContent`; no one-shot
strict-UTF-8 layer is added. A one-shot JSON success with one declared materializer does not
validate response media; media remains a build-time contract input and a runtime dispatch key only
when several materializers exist. SSE keeps its protocol-specific media/UTF-8/framing behavior.
This supersedes Q103's non-discriminator fixed-literal validation and Q68's optional-collection
explicit-null rejection, empty normalization, and defensive-copy decisions; Q103's unknown-field
tolerance and hybrid-object wall remain.

## Q107: What may curation change?

**How researched:** the pinned OpenAPI operations for `session.list`, `message.list`, and
`session.log` were compared with the TypeScript/Effect contract source at the exact same upstream
commit. `NumberFromString` decode targets and refinements (`PositiveInt`, message limit 1..200,
`Event.Seq`) appear in source, while generated OpenAPI exposes plain `string | null`; the
`order`+`cursor` prohibition appears only in description and handler code. Existing tooling then
revealed name-based `PositiveCount`, a behavior-premised `mutuallyExclusiveQueries` row, URI type
overrides, and a planned `after -> long?` override.

**Found:** this is projection loss, not pin drift: upstream's executable schema has encoded and
decoded sides, while its OpenAPI generator retained only part of that information. The C# tool
cannot consume those hidden semantics without executing/reimplementing Effect or introducing a
second machine-readable contract. Repairing them through hand-authored curation would make
upstream implementation source a silent generation input and defeat snapshot determinism.

**Decision (maintainer, sealed):** pinned `spec/openapi.json` is the sole protocol-semantic input
(ADR-0013). Upstream source remains provenance/diagnostic evidence. Curation may name/place the
represented surface, collapse proven-equivalent OpenAPI shapes, and fingerprint evidenced
exclusions; it may not invent types, formats, constraints, cross-field rules, or runtime
validation. Descriptions generate documentation only. A lossy construct stays faithful or fails
closed, and a confirmed projection defect is reported upstream instead of becoming a local
semantic override.
This supersedes Q102's `after -> long?`/non-negative decision and Session 24's #28
prose/handler-derived mutual-exclusion guard. Q102's operation-name curation and Session 24's
#32/#33 decisions remain unchanged. It also supersedes Q39's behavior-premised semantic-override
category; curation rows remain fail-closed only within ADR-0013's naming/placement/equivalence/
exclusion boundary.

## Q108: Which evidence follows from the reset?

**Decision (maintainer):** two pre-freeze investigations enter the roadmap. First, benchmark
direct `IReadOnlyList`/`IReadOnlyDictionary` model surfaces against
`ImmutableArray`/`ImmutableDictionary` across source-generated JSON, Native AOT, downlevel TFMs,
request ergonomics, and allocation/throughput; `FrozenDictionary` is only a comparison control for
long-lived read-heavy data. `IReadOnly*` remains the shipped shape unless immutable collections
show a compelling total-cost win. Second, at the next sanctioned spec refresh, independent
read-only passes compare current upstream Effect schemas, generated OpenAPI, and first-party
generated client types. Verified losses are canonicalized and filed upstream after duplicate
search and maintainer review. Seed cases are `message.list.limit` (integer 1..200),
`session.list.limit` (`PositiveInt`), and `session.log.after` (`Event.Seq`) appearing only as strings
in OpenAPI. The diagnostic report never becomes generator or curation input.

# Session 30 — 2026-08-18: in-band JSON-null representation

## Q109: Must every admitted JSON null materialize as CLR null?

**How researched:** the pinned `Shell.Info.metadata` schema was traced through the free-form-object
projection, type-plan binding, Roslyn type emission, and reflection-disabled source-generated JSON
context. The exact upstream pin supplied diagnostic controls only: `Shell.Metadata` is
`Schema.Record(Schema.String, Schema.Unknown)`, shell runtime echoes caller metadata without
inspecting values, and the generated `JsonValue` wire type explicitly includes null. A
source-generated .NET probe then deserialized a null dictionary value as
`JsonElement.ValueKind == JsonValueKind.Null`, serialized it back as JSON null, and repeated the
same result after round-trip. Before the correction, the selected PublicApi contained 77
unrestricted dictionary-value signatures using `JsonElement?`.

**Found:** schema nullability and CLR nullability are not identical when the selected CLR
representation has a canonical in-band JSON-null state. `Nullable<JsonElement>` creates two CLR
representations for one wire token (`null` and `JsonValueKind.Null`) even though non-null
`JsonElement` already materializes and writes the token through source-generated metadata. Optional
outer properties still need CLR null to represent absence; present required properties, list slots,
and dictionary entries do not.

**Decision (maintainer, sealed):** null representation is capability-based rather than a
`JsonElement` name exception. A representation remains non-nullable when source-generated
serialization proves that JSON null materializes as a canonical non-null CLR state and that state
writes back as JSON null across the supported runtime matrix. `JsonElement` is the current carrier
through `JsonValueKind.Null`. Optional properties remain nullable so omission and explicit null
collapse; required properties and present collection slots use the carrier's in-band state. The
generator records this representation capability mechanically, never through endpoint/property
curation, and adds no recursive runtime validation or normalization. This narrows Q106's universal
schema-nullable-to-CLR-nullable wording without changing its presence, ownership, or materialization
boundaries (ADR-0004, ADR-0014).

# Session 31 — 2026-08-18: CI analyzer and formatting ownership

## Q110: Which gate should own semantic linting and physical formatting?

**How researched:** current Microsoft Learn guidance and the SDK 10.0.302 `dotnet format`/Roslyn
sources were compared with the effective repository MSBuild and `.editorconfig` configuration. The
solution contains 8 projects and 20 Linux target compilations. Controlled scratch probes injected
one file into the shipped SDK project without modifying project sources, then compared build,
`format whitespace`, `format style`, `format analyzers`, and bare format for IDE0055, IDE0007,
IDE0002, IDE0049, CA1822, Sonar S2325, and import ordering. Effective analyzer items and generated
globalconfig inputs were also inspected. Warm local wall/RSS measurements covered each format mode,
and no-incremental solution builds compared analyzer-on and analyzer-off cost without changing
repository policy.

**Found:** `RunAnalyzersDuringBuild=true`, `EnforceCodeStyleInBuild=true`, `AnalysisMode=All`,
warnings-as-errors, and per-rule severities make build the semantic wall for compiler,
build-enforceable IDE, SDK CA, third-party analyzer, and source-generator diagnostics across target
compilations. Build refused IDE0055, IDE0007, CA1822, and S2325 probes; whitespace did not rerun the
semantic diagnostics. Bare format creates a second MSBuild/Roslyn workspace and runs whitespace,
style, and analyzer passes; `--no-restore` skips restore only, and no `--no-build` option exists.
Full format's unique policy was import organization plus IDE0001/0002/0003/0035/0049-class cleanup
that Roslyn marks unavailable during build. Build and whitespace passed IDE0002, IDE0049, and import
order probes while style format refused them. A diagnostic-filtered style pass still ran import
organization and measured 45.84 seconds, only 9.79 seconds faster than unfiltered style; maintaining
an SDK-sensitive allow-list did not earn that small saving. Microsoft documents import preferences
among IDE0055 formatting options, but SDK 10.0.302 implements import organization as a separate
CodeStyle-category formatter: build IDE0055 and whitespace passed the misordered-import probe while
style failed `IMPORTS`. That documented-versus-observed split is why the style gate remains.

Warm local full/whitespace/style/analyzer format elapsed times were 117.00/11.66/55.63/98.53
seconds, with 2.53 GB/398 MB/1.43 GB/2.40 GB maximum RSS. Hosted Linux full format took 3:56, 3:53,
and 3:00 in the three sampled runs. A no-incremental local solution build measured 42.96 seconds
with analyzers and 10.72 without. That cost does not justify disabling analyzers on an OS or TFM:
Windows has two additional net472 test compilations, and equivalent coverage needs its own design.
The explicit NetAnalyzers package plus `EnableNETAnalyzers=true` resolved to one effective SDK
NetAnalyzers assembly on both net10.0 and net472, not duplicate analyzer execution. The active SDK
globalconfig was `analysislevel_10_all_warnaserror`, confirming the intended pinned All mode.
Third-party ownership follows mechanically rather than from the single Sonar probe: all analyzer
packages inherit through `Directory.Build.props`, `TreatWarningsAsErrors` and
`CodeAnalysisTreatWarningsAsErrors` are global, no `WarningsNotAsErrors` escape exists, and the two
project `NoWarn` rows apply identically to build and format.

**Decision:** build owns compiler, SDK CA, third-party analyzer, source-generator, and
build-enforceable IDE diagnostics. The former bare format gate splits into
`dotnet format whitespace --verify-no-changes --no-restore` for UTF-8, LF, final newline, trailing
whitespace, and syntax formatting, plus
`dotnet format style --verify-no-changes --no-restore --severity warn` for import organization and
all configured warning/error Roslyn style rules. The style pass intentionally has no diagnostic
allow-list, so SDK rule changes cannot silently escape CI. This preserves IDE0001/0002/0003/0035
and IDE0049 as errors while avoiding the expensive solution-wide SDK CA and third-party
format-analyzer pass. Generation remains a distinct mutating pipeline:
after writing source, `GenerationWriter` runs project-scoped full format over only generated paths
to canonicalize committed output. Analyzer execution remains enabled for every current target and
OS pending a separate coverage-preserving build optimization. This supersedes Q18's single bare
`dotnet format --verify-no-changes` CI clause; its rejection of CSharpier and advisory line-width
decision remain unchanged.

# Session 32 — 2026-08-19: typed stream failure causes

## Q111: Is the pinned `Fail.error: not: {}` branch an inconsistent OpenAPI schema?

**How researched:** traced both pinned stream declarations and handlers at upstream commit
`a6a712a`, opencode's generated Effect and Promise clients, its `httpapi-codegen` stream walls, and
the exact `effect@4.0.0-beta.101` `HttpApiSchema`, OpenAPI, server-builder, and client-builder
sources.

**Found:** the shape is valid and deliberate generic specialization, not an inconsistent document.
`HttpApiSchema.StreamSse({ data })` defaults its typed error schema to `Schema.Never`; Effect emits
`errorSchema` from that schema and `causeSchema` from `Schema.Cause(error, Schema.Defect())`. The
generic `Fail | Die | Interrupt` structure therefore becomes `Fail<Never> | Die<Defect> |
Interrupt`: `Fail` remains syntactically declared but denotes the empty set. The `session.log`
handler additionally applies `Stream.orDie`, so normal typed failures become defects. Effect's
server encodes a full cause under the reserved SSE event and its client preserves the event name,
decodes the same cause schema, and re-fails the stream with that cause.

**Found:** opencode's generated Promise client explicitly admits only SSE declarations whose error
schema is Never, but its reader keeps only `data:` lines and discards `event:`/`id:`. A reserved
failure cause is consequently yielded as an unchecked ordinary payload. The current
`event.subscribe` handler separately writes data and heartbeat frames through `handleRaw`, but the
pinned extension remains the sole protocol contract; neither implementation difference is a local
endpoint-curation input (ADR-0013).

**Decision (maintainer, sealed):** project exact standalone `not: {}` as a never node and refuse
other `not` forms at the keyword pointer. Generic inhabitation analysis makes an object with a
required never member impossible. Preserve its declared marker as a known-impossible generated
dispatch entry, emit no public dead variant, and refuse the marker as protocol-invalid rather than
using ADR-0009's genuinely-unknown carrier. Hand-written machinery contains no operation-ID,
generated-type-name, or literal-tag condition (ADR-0015).

## Q112: How does a schema-valid stream failure reach .NET consumers?

**Decision (maintainer, sealed):** generated adapters expose source-generated metadata for the
bound cause array. A valid reserved frame throws `OpenCodeStreamFailureException`, preserving
existing `OpenCodeTransportException` catch behavior while exposing typed reasons through `Cause`.
Malformed JSON, null cause materialization, and known-impossible tags remain plain protocol
failures. The shared hand-written marker lets the runtime seam remain generic while each generated
cause union retains its own typed discriminator interface (ADR-0015).

# Session 33 — 2026-08-19: Arc 3a deliverable closure

## Q113: What does the complete session-log stream path cost before Arc 3b adds breadth?

**How measured:** added `SessionLogStreamBenchmarks` to the permanent BenchmarkDotNet coverage
suite and ran both a Dry validation and the default job from a clean copy of the current tree. Each
invocation calls generated `SessionClient.GetLogAsync` over a canned 200 `text/event-stream`
response, crossing request construction and decoration, `Pipeline`, `ServerSentEventReader`, the
generated `SessionLogResponseStreamAdapter`, source-generated JSON metadata, and union dispatch.
Global setup refuses either arm unless every item materializes as its expected generated type. The
two wire-shaped workloads cover 1,024 shallow `EventLogSynced` payloads at 55 bytes each
(64,512-byte framed body) and 64 larger, nested, schema-valid `SessionCreated` payloads at 2,344
bytes each (150,528-byte framed body). Because frame count, payload size, and model depth all differ,
the arms are coverage baselines rather than causal isolation of any one cost. No network or server
time enters the measurement.

**Environment and result:** BenchmarkDotNet 0.15.8 on Ubuntu 26.04, Linux 7.0.0-29-generic,
AMD Ryzen Threadripper 2970WX limited to 12 physical/logical cores, .NET SDK 10.0.302, and .NET
10.0.10 x64 RyuJIT x86-64-v3 with concurrent workstation GC. The default job measured the 64 large
frames at 1.157 ms/op (0.0243 ms error, 0.0715 ms standard deviation) and 717.82 KB/op; the 1,024
small frames measured 1.674 ms/op (0.0318 ms error, 0.0366 ms standard deviation) and 507.20 KB/op.
BenchmarkDotNet flagged the large-frame distribution as bimodal (`mValue = 3.83`) and removed one
small-frame outlier while detecting two (1.58 ms and 1.97 ms); the figures above are its default
processed summary.
These are coverage baselines, not before/after optimization claims: one operation consumes one
complete canned stream, so allocations and elapsed time are per complete response rather than per
frame.

**Decision:** keep the end-to-end class beside the parser-only benchmark. The parser benchmark
continues to isolate framing mechanics; this benchmark guards the integration path Arc 3a actually
ships. No optimization or stream-protocol expansion follows from the baseline alone.

## Q114: Does the Generic Host example consume a real session log and stop through host cancellation?

**How demonstrated:** `npx` resolved the exact pinned-compatible npm package
`@opencode-ai/cli@0.0.0-next-17403`; the example's typed health call identified that same version
and server PID 415977. The server and example were run from the repository root on Linux:

```bash
OPENCODE_SERVER_PASSWORD=123456 \
  npx --yes @opencode-ai/cli@0.0.0-next-17403 serve --hostname 127.0.0.1 --port 41999
OPENCODE_SANDBOX_ENDPOINT=http://127.0.0.1:41999 \
  OPENCODE_SERVER_PASSWORD=123456 \
  dotnet tests/OpenCode.Sdk.Sandbox/bin/Release/net10.0/OpenCode.Sdk.Sandbox.dll --stream
```

The Extensions package registered the singleton client family in a Generic Host; the hosted worker
received `SessionsClient`, created session `ses_fe69038d1ffe89PeDlv0cRBBDo`, obtained its bound
`SessionClient`, and called `GetLogAsync` with `Follow=True` and `BackgroundService`'s
`stoppingToken`. The observed frame materialized as generated `EventLogSynced`. A SIGINT after five
seconds produced the host's `Application is shutting down...` path without a transport/protocol
failure; the separately launched server remained available until it received its own SIGINT, after
which health refused the connection and `opencode2 service status` remained `stopped`.

**Environmental limits:** the ordinary server leaves `events.persist` false: durable commits
advance the aggregate sequence but historical payload rows are not retained, so the repeat observed
the typed `log.synced` watermark rather than replayed durable event payloads. It did not deliberately
inject a reserved failure frame, network cut, malformed frame, or unknown variant; deterministic
contract tests own those paths. The example creates a session on each run and does not delete it
during host shutdown. This is one Linux process-lifecycle demonstration, not the three-OS hosted
acceptance required of the source increment.

**Closure verification:** an independent review found six actionable example, benchmark, and
evidence issues; all were corrected before the source checkpoint. A final fresh-context review found
no substantive defect, and its one low-severity statistical-completeness note produced the
bimodal/outlier disclosure above. Slopwatch, Release build, whitespace, style, and all 1,290 local
test executions passed with none failed or skipped. Source commit `c7a35bd` then passed hosted run
`32240794296` on Linux, Windows, and macOS.

# Session 34 — 2026-08-19: Arc 3b structural-union decision

## Q115: How should the first selected heterogeneous structural unions appear in .NET?

**Trigger:** selecting `v2.event.subscribe` reached two pinned untagged value unions that the
generator had deliberately refused: `Form.Value1` (`string | special-number | boolean | string[]`)
and `Form.When1.value` (`string | special-number | boolean`). This was the public-API decision parked
in the roadmap, not an endpoint transport defect. The simultaneously reached
`tui.command.execute.data.command` is only an open string refinement; resolving its promoted enum
reference lets the existing same-primitive policy collapse it to `string` without emitting the dead
enum branch.

**Diagnostic upstream evidence:** the pinned upstream Effect schema declares `Form.Value` as
`String | Number | Boolean | Array(String)`, and its generated TypeScript client exposes the same
compile-time union. Under ADR-0013 this corroborates the projection but contributes no wire type,
dispatch precedence, or generation rule; those come only from the pinned OpenAPI graph.

**Probe:** two reflection-disabled System.Text.Json prototypes exercised the exact token matrix. A
single carrier plus `Kind` and guarded arms produced two public concepts per site; a base plus one
record per arm produced six for the four-arm value. Both round-tripped text, finite number, boolean,
text list, and raw unknown object values; both kept `"NaN"` in the earlier text branch, refused a
malformed claimed text-list arm, and refused writing `double.NaN` as the ordinary number arm.

**Decision (maintainer, sealed):** emit the single generated carrier shape in ADR-0016. Dispatch is
by JSON token kind in pinned branch order. A claimed token followed by malformed content is a
protocol failure; an otherwise unclaimed valid non-null value token uses the raw unknown arm. Ambiguous
same-token branches fail binding. Marked object unions remain interfaces under ADR-0011, whose
scope now includes side-by-side marked and structural examples.

**Curation check:** a live `SchemaNodeComparer` probe rejected `Form.When1`, the numeric field
schemas, `Form.Info1`, and `Form.Value1` as aliases of their unsuffixed names. After removing those
invalid assumptions, dependent field/union/answer aliases also failed. Only
`Form.Metadata1 -> Form.Metadata` is structurally identical under the current fail-closed graph and
was retained. Alias identity and .NET naming remain separate: reason-bearing `schemaNames` rows remove
the spec-gen suffix from the selected public Form family without claiming the wire schemas are equal.
The resolver's ordinary owner collision fails closed if an unsuffixed twin later joins the profile;
naming aesthetics therefore did not weaken the alias wall.

## Q116: Does the generated global event subscription observe typed live frames and cancel cleanly?

**How demonstrated:** launched exact pinned-compatible `@opencode-ai/cli@0.0.0-next-17403` on Linux
at `127.0.0.1:41999` with the sandbox password, then ran the committed Generic Host worker over
`EventsClient.SubscribeAsync(stoppingToken)`. Its typed health call identified server version
`0.0.0-next-17403` and PID `597763`. A separate no-mode sandbox invocation exercised the breadth
walkthrough and created session `ses_fe573d9ecffeIOfpw1CScSgYtI`, ensuring activity occurred only
after the volatile subscription was open.

```bash
OPENCODE_SERVER_PASSWORD=123456 \
  npx --yes @opencode-ai/cli@0.0.0-next-17403 serve --hostname 127.0.0.1 --port 41999
OPENCODE_SANDBOX_ENDPOINT=http://127.0.0.1:41999 \
  OPENCODE_SERVER_PASSWORD=123456 \
  dotnet tests/OpenCode.Sdk.Sandbox/bin/Release/net10.0/OpenCode.Sdk.Sandbox.dll --events
```

**Observed:** the generated event adapter materialized `EventServerConnected` with tag
`server.connected`, then the shared durable/live leaf `SessionCreated` with tag `session.created`.
SIGTERM drove Generic Host's normal `Application is shutting down...` cancellation path, closed the
open response, and exited the worker while the separately launched server remained healthy with the
same identity. Stopping that server separately made health refuse the connection.

**Environmental limits:** the worker was started in the background through `nohup`; that shell setup
inherited ignored SIGINT, so cancellation evidence uses SIGTERM rather than pretending Ctrl+C was
observed. This is one Linux live demonstration. The global bus is volatile and has no filter, cursor,
replay, resume, or reconnect contract; the run observed two known frames and did not induce overflow,
a reserved failure frame, malformed payload, unknown variant, or network cut. Deterministic contract
tests own those paths. The breadth trigger created a session and did not delete it.

# Session 35 — 2026-08-20: Arc 4 paginator

## Q117: What public pagination shape should the SDK add?

**How researched:** current Azure.Core `AsyncPageable<T>`/`Page<T>`, System.ClientModel
`AsyncCollectionResult<T>`, Google GAX `PagedAsyncEnumerable<TResponse, TResource>`, AWS SDK for
.NET v4 paginators, Microsoft Graph `PageIterator`, and Stripe.net auto-pagination were compared
against the existing `ListMessagesAsync`, `MessageListResponse`, `ListRequest`, `ListCursor`,
multi-TFM, error, and generator contracts. Azure and System.ClientModel expose item iteration plus
page/raw-response access; Google follows the same two-level pattern; AWS exposes item and full-
response sequences; Stripe keeps its explicit list call and adds an item-only auto-paging sequence;
Graph's callback state machine follows full next-link URLs and does not fit opaque query cursors.
Primary evidence: [Azure pagination](https://learn.microsoft.com/dotnet/azure/sdk/pagination),
[System.ClientModel](https://learn.microsoft.com/dotnet/api/system.clientmodel.asynccollectionresult-1),
[Google GAX 4.10](https://cloud.google.com/dotnet/docs/reference/Google.Api.Gax/latest/Google.Api.Gax.PagedAsyncEnumerable-2),
[AWS SDK v4](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/paginators.html),
[Microsoft Graph](https://learn.microsoft.com/graph/sdks/paging), and
[Stripe.net](https://docs.stripe.com/api/pagination/auto?lang=dotnet).

**Found:** an Azure-shaped local `Page<T>` would duplicate `MessageListResponse`, flatten the pin's
`previous`/`next` cursor into a one-token convention, and encourage an `int` page-size hint where the
pin exposes `limit` only as a string. The concrete additional benefit over the existing page method
would be automatic page-metadata traversal; no current consumer requires it, and it remains
additive. Plain `IAsyncEnumerable<TItem>` needs no dependency or second public vocabulary and is
already supported on downlevel targets through the SDK's existing async-interfaces package.

**Decision (maintainer, sealed):** keep every endpoint-specific one-page `List*Async` API and add a
generated `Enumerate*Async` item sequence when the binder mechanically proves the supported cursor-
list dialect (ADR-0017). `v2.message.list` is the proving slice and emits
`EnumerateMessagesAsync(MessageListRequest?, CancellationToken)`. The first request is unchanged;
continuations retain the exact string `Limit`, clear first-page-only `Order`, and carry opaque
`Cursor.Next`. Only null `Next` ends traversal, errors always throw at their page boundary, and
`Previous` stays on the explicit page path. Different pagination shapes are not guessed or folded
into this mechanism.

## Q118: Does the generated paginator preserve the pinned traversal and existing page seam?

**Implementation:** the binder creates a `PaginationPlan` only for the exact `ListRequest` query
profile plus a cursor-list response and an operation signature the current core can call without an
endpoint-specific transport path. The generated response adapter implements the internal page
projection contract; generated `EnumerateMessagesAsync` delegates the existing virtual
`ListMessagesAsync` method, its initial request, and that adapter into the hand-written
`CursorPaginator`. The core owns lazy page fetches, item flattening, cancellation between buffered
items, null-only termination, and continuation sequencing. The adapter owns only pin-bound member
projection and construction of the next `MessageListRequest`.

**Deterministic evidence:** a three-response contract starts with an empty page carrying `next`,
then materializes user and shell messages. It proves no request occurs before enumeration, the
initial `limit=2&order=asc&cursor=...` is sent unchanged, and continuations are exactly
`limit=2&cursor=...` without `order`. A later-page 400 remains a typed `InvalidCursorError`; a
cancelled enumeration stops between two buffered items; and a protected-constructor subclass proves
the paginator invokes the virtual page method while preserving an empty cursor as present opaque
data. Binder evidence pins the selected positive plan, rejects a curated non-`List*Async` name, and
keeps the extra-filter `session.list` request outside this exact dialect. Synthetic output emits,
source-generates, and compiles with the hand-written core.

**Local closure:** an independent fresh-context review reported no defects. Slopwatch found zero
issues; Release build completed with zero warnings and errors; whitespace and warning-level style
verification passed; all 1,338 local test executions passed with none failed or skipped; both tool
entry smokes and generation verification passed. Source commit `ec043f4` then passed hosted run
`32338694450` on Linux, Windows, and macOS.

**Live evidence:** exact `@opencode-ai/cli@0.0.0-next-17403` served `127.0.0.1:41999`; generated
health identified version `0.0.0-next-17403` and server PID `995554`. The committed sandbox's
`--paginate` mode read existing historical session `ses_ff14d29a5ffeqrxEFlSnZf3btP` with
`Limit = "1"` and ascending order. `EnumerateMessagesAsync` materialized
`SessionMessageModelSelected/model-switched` and then `SessionMessageUser/user`, proving a second
HTTP page was reached before the example stopped at two items. The run created no session or
message, invoked no provider/model, and mutated no server state; it reused real data already present
from earlier live probes. The separately launched server was then terminated and health refused the
connection. This is one Linux demonstration, not hosted three-OS evidence; deterministic tests own
error, cancellation, empty-page, and opaque-cursor edge paths.

# Session 36 — 2026-08-20: Arc 5 owned-transport/net472 GA cluster

## Q119: How does the singleton-owned transport remain safe across redirect, connection, and route boundaries?

**Trigger and decision:** issue #43's real-loopback reproduction showed that the owned handlers
followed a 302 into a typed 200 response, while the net472 path retained the two-connection
`ServicePoint` ceiling and infinite connection lease. The sealed #32 decision separately required
uniform refusal before `Uri.EscapeDataString`. Arc 5 kept ADR-0010's options-only public shape and
closed the mechanisms together because redirect policy, connection ownership, and response cleanup
meet inside the same pipeline.

**Transport implementation:** every owned handler now disables automatic redirects. The pipeline
classifies any surfaced 3xx as a protocol/transport failure before reading its body, on both one-shot
and SSE paths; `NoThrow` cannot suppress it. Modern handlers retain their 120-second
`PooledConnectionLifetime`, now directly observable through the real handler factory. Downlevel
targets configure the endpoint-and-current-proxy `ServicePoint` with `ConnectionLimit = int.MaxValue`
and `ConnectionLeaseTimeout = 120000` both at owned construction and immediately before each owned
send. Reapplication survives idle `ServicePoint` scavenging and ambient proxy changes without
mutating `ServicePointManager` defaults. The selected endpoint/proxy `ServicePoint` is itself
process-shared, so its policy also affects other process traffic to that pair.

**Compatibility correction from review:** a first cut assigned
`HttpClientHandler.CheckCertificateRevocationList` through the netstandard2.0 asset. Fresh-context
review identified that older .NET Framework implementations able to load that asset lack the runtime
member, creating a compile-green `MissingMethodException` risk and an unrelated TLS-policy change.
The assignment was removed. A file-scoped CA5399 arbitration records that redirect ownership wins
without forcing a new revocation policy; the analyzer remains enabled elsewhere. The same review
caused downlevel `ServicePoint` policy to widen beyond the NET472 compile symbol and reapply per send,
made the net472 stream-open assertions non-vacuous, isolated global-proxy tests, and documented the
process-shared endpoint effect. The generated-source compiler now defines its actual default-target
`NET` symbol rather than accidentally compiling the downlevel SDK branch against net10 references.

**Route implementation:** generated path segments and every query/deep-object value pass through one
`RouteValuePolicy` before escaping. Inputs over 32,766 UTF-16 code units or containing a lone high or
low surrogate throw `ArgumentException` consistently on every TFM. Exactly 32,766 code units and
valid surrogate pairs remain accepted. Existing path null/blank/dot-segment guards and the empty
opaque cursor remain unchanged. SDK-owned query names use the same centralized escaper with their
representability asserted as an internal invariant. The ambient location header stayed outside #32's
route boundary.

**Ownership and real-handler evidence:** a raw loopback HTTP/1.1 fixture crosses the platform's real
owned handler. It proves that a healthy redirect target is never requested, observes modern handler
redirect/lifetime policy, verifies direct and proxy-backed net472 `ServicePoint` settings, and keeps
two same-authority SSE responses open while an ordinary net472 request completes. Separate response
and content trackers prove disposal after success, API error, redirect protocol failure under
`NoThrow`, and caller cancellation after response acquisition. Streaming 3xx classification has its
own deterministic test.

**Closure:** PublicApi and generated-manifest membership are unchanged. Slopwatch reported zero
issues; the Release build compiled every package TFM with zero warnings/errors; whitespace and
warning-level style verification passed; all 1,374 local executions passed with zero failed or
skipped; both tool entry smokes and generation verification passed. Source commit `b261014` passed
hosted run `32350952168` on Linux, Windows, and macOS; the Windows leg executed the net472 runtime
evidence. Issues #43 and #32 closed with that commit/run. No Arc 6, launcher, M5, telemetry, retry,
hook, spec-refresh, or MCP work entered the increment.

# Session 37 — 2026-08-20: Arc 6 measured performance pass

## Q120: Which response, union, collection, and downlevel costs justify the final M3 changes?

**Method:** fresh baseline and final BenchmarkDotNet 0.15.8 default jobs ran from clean copies of
the source before and after Arc 6, pinned to the same AMD Ryzen Threadripper 2970WX environment
(12 physical/logical cores), Ubuntu 26.04 / Linux 7.0.0-29, .NET SDK 10.0.400, and .NET 10.0.11
x64 RyuJIT x86-64-v3 with concurrent workstation GC. Every class retained `MemoryDiagnoser`; Dry
runs preceded default jobs. Allocation is the primary comparison because timing moved with host
load. The final results were:

| Complete operation | Baseline mean / allocated | Final mean / allocated |
|---|---:|---:|
| `GetMessageAsync` | 45.970 us / 21.91 KB | 39.728 us / 19.15 KB |
| `GetHealthAsync` | 1.736 us / 2.08 KB | 1.805 us / 2.05 KB |
| `ListMessagesAsync` | 46.859 us / 22.60 KB | 43.593 us / 19.81 KB |
| Deep known-union deserialize | 41.99 us / 13.35 KB | 37.54 us / 12.72 KB |
| 64 large parser-only SSE frames | 777.8 us / 321.54 KB | 650.8 us / 321.54 KB |
| 1,024 small parser-only SSE frames | 390.1 us / 177.69 KB | 396.4 us / 177.69 KB |
| 64 large end-to-end session-log frames | 1.100 ms / 717.82 KB | 1.002 ms / 713.32 KB |
| 1,024 small end-to-end session-log frames | 1.678 ms / 507.20 KB | 1.428 ms / 435.20 KB |

The parser-only arms are controls: their allocation is byte-for-byte unchanged, and the large arm's
timing distribution remained noisy. No health throughput improvement is claimed; its means moved in
the wrong direction by about four percent while error/variance overlapped, but allocation did not
regress. The retained one-shot byte path earns its place from the roughly 12 percent allocation
reduction on representative message/list responses, while copied-reader known-union dispatch earns
its place independently in the isolated and end-to-end stream arms. Unknown variants alone keep the
DOM required by ADR-0009.

**Response and carrier contracts:** declared successes select a generated UTF-8 adapter before
error buffering. Valid UTF-8 materializes from bytes; an invalid charset remains a transport error;
non-UTF-8 charset/BOM input and malformed UTF-8 retain `HttpContent`-equivalent decoding and
replacement behavior. Error responses remain decoded strings and preserve `RawBody` under both
throwing and `NoThrow` channels. The one-shot `HttpClient.Timeout` budget now covers send through
body consumption, while caller cancellation remains distinct. Timeout/cancellation disposes content
and observes the retained real body-read task; downlevel targets deliberately use the parameterless
`HttpContent` read so Polyfill cannot hide that task behind its own cancellation wrapper. Declared
no-content success reads no body but still disposes it. #33's public unknown carriers refuse
non-object, missing, whitespace, fixed-marker, or discriminator disagreement; wire reads and writes
remain payload-only replay. Duplicate top-level discriminators preserve the prior last-value rule.

**Generated collection comparison:** a separate reflection-disabled source-generation probe used
256-item list and dictionary DTOs. Direct `IReadOnlyList` construction allocated 24 B versus 2,096 B
for `ImmutableArray`; deserialization allocated 58.77 versus 60.84 KB, while serialization was
allocation-equivalent. Direct `IReadOnlyDictionary` construction allocated 24 B versus 16,576 B for
`ImmutableDictionary` and 30,896 B for `FrozenDictionary`; deserialization allocated 86.39 versus
102.48 KB and immutable serialization was 45 percent slower with effectively equal allocation.
`FrozenDictionary` improved one lookup from 13.14 to 12.02 ns, but that long-lived read-heavy control
did not repay its construction cost or become a DTO candidate. Both candidate families round-tripped
through source-generated metadata in a published/run Linux Native AOT probe and compiled for
netstandard2.0/net472; immutable downlevel use also required a new direct
`System.Collections.Immutable` dependency. The shipped shallow `IReadOnly*` API therefore remains
unchanged.

**Downlevel append evidence:** Linux could not execute a net472 BenchmarkDotNet process, so a
same-runtime source-equivalent probe compared Polyfill's exact whole-line `ToString` path with the
dedicated-buffer algorithm, including one buffer allocation per complete response. Across 1,024
small lines it moved from 37.73 us / 144.38 KB to 27.45 us / 16.41 KB; across 64 large lines it moved
from 49.90 us / 304.82 KB to 15.74 us / 25.35 KB. The production `!NET` branch uses that dedicated
buffer, never aliases unread decoder output, and a cross-buffer long-line/next-frame contract test is
eligible for the hosted Windows net472 leg. These numbers are mechanism evidence on the same CPU,
not a claim that net10 timings equal .NET Framework timings.

**Review and local closure:** fresh-context adversarial review found a decoder-buffer alias, an
unmanaged timed-out body read, a downlevel Polyfill task-wrapper gap, and a non-object constructor
exception leak; all were corrected and the follow-up review reported no remaining substantive
finding. PublicApi and generated-manifest membership are unchanged. Slopwatch reported zero issues;
Release build, whitespace, warning-level style, both tool entry smokes, and generation verification
passed. All 1,413 local modern-TFM executions passed with zero failed or skipped. Source commit
`fa6124d` then passed hosted run `32374393085` on Linux, Windows, and macOS; the Windows leg executed
the net472 timeout/cancellation and long-line SSE evidence. Issues #23, #29, and #33 are closed. M3
is complete; no M4, M5, telemetry, retry, hooks, spec-refresh, or MCP source work entered the pass.

# Session 38 — 2026-08-20: benchmark observability follow-up

## Q121: What do the permanent benchmarks measure per wire byte, and how does the suite attribute cost to a component?

**Method:** the maintainer's standing concern was that reports showed allocated KB without the bytes that
arrived on the wire. The exact-byte baseline for `fa6124d` was first recovered from a clean worktree with
BenchmarkDotNet 0.15.8 default jobs and the full JSON exporter (`Memory.BytesAllocatedPerOperation`),
on the Q120 environment (Threadripper 2970WX limited to 12 cores, Ubuntu 26.04, Linux 7.0.0-29, SDK
10.0.400, .NET 10.0.11, concurrent workstation GC). BenchmarkDotNet's `KB` column is KiB: every Q120
allocation row reproduces byte for byte (19,608 B = 19.15 KB, 2,104, 20,288, 13,024, 329,256, 181,952,
730,440, 445,648). Wire sizes were reproduced mechanically from the fixture builders: the deep assistant
message is 2,182 B after trailing-newline trimming; its `{"data":...}` envelope 2,191 B; its single-item
cursor page 2,228 B; health 49 B; 64 framed large frames 140,160 B (2,190 B/frame); 1,024 framed small
frames 71,680 B (70 B/frame, 62 B payload); session-log large 150,528 B (2,344 B payload) and small
64,512 B (55 B payload).

**Wire-to-allocation for the prior eight cases (exact bytes, same environment):**

| Operation | Wire B/op | Alloc B/op | Alloc/Wire | Excess B | Per item |
|---|---:|---:|---:|---:|---:|
| `GetMessageAsync` (deep) | 2,191 | 19,608 | 8.95x | 17,417 | 1 message |
| `GetHealthAsync` | 49 | 2,104 | 42.94x | 2,055 | 1 |
| `ListMessagesAsync` (1 item) | 2,228 | 20,288 | 9.11x | 18,060 | 1 |
| Deep known-union deserialize | 2,182 | 13,024 | 5.97x | 10,842 | 1 |
| 64 large parser-only SSE frames | 140,160 | 329,256 | 2.35x | 189,096 | 5,145 B/frame |
| 1,024 small parser-only SSE frames | 71,680 | 181,952 | 2.54x | 110,272 | 178 B/frame |
| 64 large session-log frames end to end | 150,528 | 730,440 | 4.85x | 579,912 | 11,413 B/frame |
| 1,024 small session-log frames end to end | 64,512 | 445,648 | 6.91x | 381,136 | 435 B/frame |

A small payload's ratio is high because the per-operation fixed cost (request, headers, URI, handler
response, async state, body buffer growth) does not shrink with the body: the 49-byte health call pays
the same ~1.8 KB pipeline floor as a 2 KB message call.

**Suite redesign:** every class now owns one operation family and decomposes it as a component ladder
over `WireFixture` inputs (name, exact body, item count, payload bytes per item, declared charset)
supplied through `[ParamsSource]` (ladder classes) or `[ArgumentsSource]` (heterogeneous cases). Custom
columns derived from the fixture and `GcStats.GetBytesAllocatedPerOperation` print `Wire B`, `Items`,
`Payload B/item`, `Alloc B/item`, and `Alloc/Wire` beside an exact-byte `Allocated` column
(`SummaryStyle.WithSizeUnit(SizeUnit.B)`), and the full JSON export is always on. Fixtures are composed
from the one deep seed: marker-last, duplicate-marker-last-known, unknown-marker, 120- and 2,400-part
messages (54,150 and 1,075,590 B), and 1/30/480-item pages (2,228 / 65,535 / 1,047,885 B); session-log
arms add a structured `session.tool.success` event with 16 nested tool-content parts and a 256-frame mix.
Every `GlobalSetup` refuses a fixture that does not materialize its expected generated type. BenchmarkDotNet
requires unsealed benchmark classes and re-creates complex `[ParamsSource]` values in the benchmark
process by index, so fixture sources are deterministic. The run must start from a clean copy outside the
repository: BenchmarkDotNet locates the project by name from the solution root and refuses the duplicate
copies under `.scratchpad/`. Classes: `HealthBenchmarks`, `NoContentBenchmarks`, `MessageGetBenchmarks`,
`MessageListBenchmarks`, `PaginationBenchmarks`, `SessionCreateBenchmarks`, `ErrorChannelBenchmarks`,
`UnionDeserializationBenchmarks`, `ResponseEncodingPolicyBenchmarks`, `ServerSentEventReaderBenchmarks`,
`SessionLogStreamBenchmarks`, `EventStreamBenchmarks` — 78 default-job cases, about 35 minutes on this
machine (Dry validation first, about one minute). `ClientOperationBenchmarks` was replaced; its three
methods live on under the same names inside the health, message-get, and message-list ladders and
reproduce the Q120 bytes exactly.

**Ladder results (default job, same environment, mean ± SD, exact bytes):**

| Ladder | Complete operation | Pipeline without materialization | Generated adapter | Source-generated materialization |
|---|---:|---:|---:|---:|
| Health 49 B | 1.78 µs / 2,104 B | 1.14 µs / 1,840 B | 512 ns / 304 B | 471 ns / 256 B |
| Message deep 2,191 B | 42.2 µs / 19,608 B | 1.96 µs / 6,112 B | 37.4 µs / 13,216 B | 37.3 µs / 13,168 B |
| Message medium 54,159 B | 923 µs / 433,048 B | 16.0 µs / 110,048 B | 884 µs / 322,720 B | 886 µs / 322,672 B |
| Message large 1,075,599 B | 20.0 ms / 8,584,244 B | 532 µs / 2,154,817 B | 19.7 ms / 6,428,328 B | 19.0 ms / 6,428,280 B |
| Page 1 / 30 / 480 items | 43.4 µs / 20,288 B; 1.17 ms / 525,112 B; 21.6 ms / 8,359,983 B | — | 36.9 µs / 13,936 B; 1.11 ms / 392,152 B; 19.5 ms / 6,260,728 B | 37.7 µs / 13,880 B; 1.19 ms / 392,096 B; 20.2 ms / 6,260,672 B |

The no-content 204 operation costs 802 ns / 1,288 B and the bare `HttpClient` send over the same canned
handler 270 ns / 520 B, so the SDK's own one-shot floor is roughly 530 ns / 770 B and the harness is in
every number at 520 B. Reading a body through the pipeline without materializing it costs 2.0x the wire
bytes at every size (`ReadAsByteArrayAsync` buffers once and copies once), materialization costs about
5.97x the payload bytes and 88-96 percent of the elapsed time from 2 KB upward, and the generated
adapter boundary adds one response record (48-56 B). The paginator adds 256 B per traversal and no
measurable time over a hand-written page loop (1.165 vs 1.174 ms over three 10-item pages). The POST
`CreateSessionAsync` costs 5.98 µs / 4,592 B over a 372-byte envelope, of which request serialization is
191 ns / 56 B. The declared error channel costs 2.58 µs / 2,496 B under `NoThrow` and 10.9 µs / 3,608 B
when the same 404 throws `OpenCodeApiException`; the tolerant typed-error read alone is 1.05 µs / 328 B.

**Component observations:** the UTF-8 validation pass is vectorized and effectively free (53 ns for
2,182 B, 958 ns for 54,150 B, zero allocation); the UTF-8-BOM and declared-`utf-8` variants stay on the
byte path; a UTF-16 body decodes in 1.1 µs / 4,392 B; malformed UTF-8 pays an exception on the
replacement path (5.6 µs / 5,416 B). Interface dispatch of the deep message costs 36.1 µs / 13,024 B
against 29.5 µs / 12,984 B for the concrete record (the top-level discriminator scan is about 20 percent
of the time at every size: 877 vs 725 µs medium, 19.4 vs 16.0 ms large) and 40 B for the marker string;
marker position barely matters because the last-value rule scans to the end anyway (37.6 µs marker-last
vs 36.1 marker-early); a 55-byte `log.synced` item costs 999 ns / 272 B through the union versus
496 ns / 224 B concrete; an unknown marker retains its DOM for 13.3 µs / 4,504 B. The SSE reader's fixed
cost per enumeration is 26,312 B for a single small frame and 52,416 B for a single 2,182-byte frame
(8 KiB byte buffer, 16 KiB char buffer, builder growth, per-frame string); socket-sized 1,460-byte
reads cost the same as whole reads; the multi-line data form costs the same as one-line frames. In the
session-log ladders the request pipeline adds about 1.9 KB and 2-8 percent per stream, while framing
is 24-64 percent of elapsed time and per-frame materialization alone is 272 B (synced), 5,904 B
(created-2048), 8,432 B (tool-success-16), and 3,232 B (mixed) per frame. The live-bus subscription
over 1,024 idle events (85 B payload each, 1,024 x 93 B framed) costs 2.38 ms / 1,191,384 B
(1,163 B/frame); per-frame materialization through the 87-branch `IEvent` union alone is
1.84 ms / 944 B per frame against 1.15 ms / 896 B into the concrete `SessionIdle` record, so union
dispatch adds 48 B and about 0.67 µs per event while 896 B of the per-event cost is materialization
of an 85-byte event into its record (worth its own attribution before the live bus is tuned).

**Limits:** timings are single-environment and within-run comparisons only; allocation remains the
primary metric. The canned handler completes synchronously, so these are the SDK's own floor rather
than production numbers with real transport suspension. The performance project now carries a
Windows-conditional net472 target (compile-validated on Linux against the reference assemblies; not
yet executed), so downlevel numbers remain the Q120 source-equivalent evidence until a Windows run
records real net472 figures.

## Q122: What did the independent Arc 6 review find, and why had the tests not caught it?

**Method:** the one-time review brief was executed as ten independent finder lenses over a read-only
worktree at `8479149`/`fa6124d` (union dispatch, success-body materialization, timeout/cancellation/
ownership, SSE reader, runtime architecture/perf, generator emission, test coverage, benchmark audit,
documentation drift, adversarial generalist), one merge pass, and adversarial verification of every
finding rated medium or higher (two lenses — refute and reproduce — for high severity): 33 agents,
85 raw findings, 58 after merge, 21 verified (18 confirmed, 2 plausible, 1 refuted), 37 low/info
observations passed through unverified, and 95 contract claims recorded as verified-correct.
Verification used file-based C# probes referencing the SDK project with the test assembly name so
`InternalsVisibleTo` applied; no repository file was modified. Temporary probes are not repository
artifacts; each finding below states the mechanism so it can be reproduced.

**Confirmed defects (severity as verified):**

| Id | Severity | Location | Mechanism | Why existing tests missed it |
|---|---|---|---|---|
| R01 | high | `Internal/Serialization/UnionDiscriminatorReader.cs:84` | `scan.Skip()` on the copied reader throws `InvalidOperationException` whenever `IsFinalBlock` is false, so every generated marked-union converter fails under `JsonSerializer.DeserializeAsync`/`DeserializeAsyncEnumerable` over a `Stream` (known and unknown arms; even a two-element array through the async-enumerable path). Regression from the pre-Arc 6 `JsonDocument.ParseValue` (`TrySkip`-based) path. SDK-internal paths deserialize complete strings/spans and are unaffected. | Every union test deserializes a complete string or span (`IsFinalBlock=true`); no test deserializes a union from a `Stream`/`PipeReader`, and no canon sentence states whether consumer-side stream deserialization of SDK models is supported. |
| R19 | high (verifier) | `Internal/Pipeline.cs:520`, `ServerSentEventReader.cs:82` | On .NET Framework the owned `HttpClientHandler` response stream is a `DelegatingStream` over `ConnectStream`, which overrides only `Read`/`BeginRead`/`EndRead`; HttpClient disposes its linked CTS after `ResponseHeadersRead`, so the handler's abort registration is dead; Polyfill's `ReadAsync(Memory, ct)` forwards to a base `ReadAsync` that cannot be cancelled. Cancelling an idle SSE enumeration waits for the next byte (no `ReadWriteTimeout` backstop on the async path). Pre-existing, not Arc 6. | Stream cancellation tests use fakes (`BlockingStream`, `CancelingStream`) that honor the token themselves or pre-cancel before the read starts; the net472 loopback test passes `CancellationToken.None`. Needs a Windows net472 loopback probe (cancel while idle, measure `MoveNextAsync` latency). |
| R17 | medium | `Internal/Pipeline.cs:504` | The stream-open error body (non-200 before the stream starts) is read with `Timeout.InfiniteTimeSpan`; with `ResponseHeadersRead` the `HttpClient.Timeout` CTS is gone after headers, so a stalled 4xx/5xx body hangs the first `MoveNextAsync` until caller cancellation, while the one-shot path gained the Arc 6 remaining-timeout budget. No rationale recorded. | `PipelineStreamTests` has no timeout case; `BlockingContent` exists and could drive it. |
| R02 | medium | `Internal/ResponseEncodingPolicy.cs:64` | `Encoding.GetEncoding("utf-7")` throws `NotSupportedException` on .NET 5+; `ResolveEncoding` catches only `ArgumentException`, and neither body-read catch filter includes `NotSupportedException`, so a `charset=utf-7` response escapes as a raw BCL exception on both success and error planes (net472 still supports UTF-7). | The only charset tests use `not-an-encoding` (`ArgumentException`) and a UTF-16 BOM body. |
| R16 | medium (perf) | `Internal/Pipeline.cs:338` | `HttpContent.ReadAsByteArrayAsync` buffers into an exact-size array and then `CreateCopy`/`ToArray` — two transient heap copies on every TFM; 2.00x wire bytes at every size in the new ladders (4.4 KB of the 19.6 KB deep-message operation; 2.15 MB and LOH for a 1 MB body). | Not a correctness gap; the old suite had no pipeline-without-materialization row to expose the slope. |
| R18 | medium (perf) | `Internal/ServerSentEventReader.cs:89` | Per-character `Accept()`/`StringBuilder.Append(char)`, per-frame UTF-16 string, UTF-8 -> UTF-16 -> UTF-8 round trip; a contract-neutral span-scanning variant (`IndexOfAny` over the decoded `char[]`, slice appends) measured 6.6x faster on the 64-large-frame parser arm and 2.4x on the 1,024-small-frame arm with unchanged allocation. | Not a correctness gap; the parser-only benchmarks measured the whole reader without a per-character attribution. |
| R12 | medium (architecture) | `Internal/Pipeline.cs:324-433` | `ReadSuccessBodyAsync`/`ReadBodyAsync` are near-identical 55-line timed-read catch ladders (drift already started); Pipeline grew to 617 lines (coding-style §1/§4). | Structural; behaviour is pinned end to end. |
| R10 | low | `Internal/ResponseEncodingPolicy.cs:57` | On net472 the success plane strips a quoted `charset="utf-8"` while the error plane (.NET Framework `HttpContent.ReadAsStringAsync`) does not; canon's "equivalent to HttpContent string decoding" holds for the .NET (Core) algorithm only. | No quoted-charset test on either plane; the Windows net472 leg never saw one. |

Verified test gaps for shipped contracts (medium): no direct `ResponseEncodingPolicy` test (UTF-8 BOM
preamble slice on the byte path, declared/quoted charset, charset plus BOM, empty body before an invalid
charset, UTF-32/UTF-16 BOM precedence) (R09); the UTF-8 span `ReadBarePayload` overload has no top-level
`null` success test although generated adapters now use it (R13); the nested unknown carrier's fixed-
outer-marker constructor guard has no runtime test (R14); no timeout/cancellation test runs against a
real handler (R15, loopback server exists); paginator cancellation between pages is untested (R20).
Documentation: ADR-0014 still calls the immutable-collection comparison an open pre-freeze question
although Q120 closed it (R04); `ROADMAP.md` has become append-only history (R05); the Arc 6 decoding
rule is restated in three canonical documents (R06, plausible); ADR-0009 gained a material decision
without moving its `Date:` and states its mechanism twice (R11, plausible). The refuted finding claimed
nested duplicate markers were untested; existing tests cover them.

**Q120 claim verdicts:** every allocation claim is supported byte for byte (Q121 table); the
`GetMessageAsync` and deep-union timing deltas exceed noise; `ListMessagesAsync` timing is weak (final-
to-final spread 1.1 µs against a 3.3 µs claimed delta); the end-to-end stream timing rows sit inside the
machine's bimodal noise band (large arm within 1.4 percent of its baseline on repeat, small arm +9.7
percent) and should not be quoted as improvements; the downlevel append numbers cannot be re-verified
because the probe is not in the repository.

**Decision (maintainer, 2026-08-20):** the confirmed defects and test gaps are repaired before M4 planning
resumes, red-test-first, with the Windows-only items (R19, R10, net472 benchmark execution) executed on a
Windows machine. Public-surface compatibility is not a constraint for these repairs; generated output
still changes only through the generator.

# Session 39 — 2026-08-21: Arc 6 repair and Windows net472 evidence

## Q123: What does the first real net472 benchmark leg show against net10.0 on Windows?

**Method:** source head `713f09a` was mirrored with `robocopy` to a clean `C:\bench` outside the
repository, excluding `.git`, `.scratchpad`, `external`, `bin`, `obj`, `BenchmarkDotNet.Artifacts`, and
`TestResults`. Building `tests/OpenCode.Sdk.Performance.Tests` in Release compiled both net472 and
net10.0; the only warnings were the expected SourceLink warnings caused by deliberately omitting `.git`.
BenchmarkDotNet 0.15.8 then ran the health, message-get, and SSE-reader ladders in one process-level
comparison:

```powershell
dotnet build tests/OpenCode.Sdk.Performance.Tests -c Release

dotnet run --project tests/OpenCode.Sdk.Performance.Tests -c Release -f net10.0 --no-build -- `
  --runtimes net472 net10.0 `
  --filter '*ServerSentEventReaderBenchmarks*' '*HealthBenchmarks*' '*MessageGetBenchmarks*' `
  --job Dry --artifacts <outside-copy>/q123-dry

dotnet run --project tests/OpenCode.Sdk.Performance.Tests -c Release -f net10.0 --no-build -- `
  --runtimes net472 net10.0 `
  --filter '*ServerSentEventReaderBenchmarks*' '*HealthBenchmarks*' '*MessageGetBenchmarks*' `
  --artifacts <outside-copy>/q123-default
```

Dry completed all 46 selected runtime cases. The default job completed the same 46 cases in 47:03.
The environment was Windows 11 `10.0.26200.9168`, AMD Ryzen 9 5900X (12 physical / 24 logical cores),
SDK 10.0.303, .NET 10.0.11, and concurrent workstation GC. The net472 job built a net472 executable
and ran it on the installed .NET Framework 4.8.1 CLR (`4.8.9337.0`); these are downlevel-target results,
not a claim that the installed CLR itself was 4.7.2. Every ratio below is net472 divided by net10.0
inside this one run.

**Health ladder (49 wire bytes, exact allocated bytes):**

| Component | net10 mean / alloc | net472 mean / alloc | Time ratio | Alloc ratio |
|---|---:|---:|---:|---:|
| Complete `GetHealthAsync` | 1.5772 µs / 2,104 B | 17.2393 µs / 6,428 B | 10.93x | 3.06x |
| Pipeline without adapter | 0.9134 µs / 1,840 B | 13.3170 µs / 6,141 B | 14.58x | 3.34x |
| Generated adapter | 0.4260 µs / 304 B | 1.5728 µs / 313 B | 3.69x | 1.03x |
| Source-generated materialization | 0.3845 µs / 256 B | 1.5248 µs / 265 B | 3.97x | 1.04x |

The small response exposes downlevel fixed transport/async overhead: most of the extra 4,324 B on the
complete call is below the adapter boundary, not in the model materializer.

**Message-get ladders (one item; wire bytes include the `data` envelope):**

| Fixture / component | Wire B | net10 mean / alloc | net472 mean / alloc | Time ratio | Alloc ratio |
|---|---:|---:|---:|---:|---:|
| Deep / complete | 2,191 | 32.793 µs / 19,608 B | 133.863 µs / 25,146 B | 4.08x | 1.28x |
| Deep / pipeline | 2,191 | 1.295 µs / 6,112 B | 13.643 µs / 10,935 B | 10.54x | 1.79x |
| Deep / adapter | 2,191 | 30.388 µs / 13,216 B | 108.235 µs / 13,631 B | 3.56x | 1.03x |
| Deep / materialization | 2,191 | 30.688 µs / 13,168 B | 109.798 µs / 13,583 B | 3.58x | 1.03x |
| Medium / complete | 54,159 | 542.019 µs / 433,048 B | 2,662.223 µs / 452,365 B | 4.91x | 1.04x |
| Medium / pipeline | 54,159 | 7.164 µs / 110,048 B | 28.908 µs / 124,790 B | 4.04x | 1.13x |
| Medium / adapter | 54,159 | 654.989 µs / 322,720 B | 2,572.153 µs / 329,853 B | 3.93x | 1.02x |
| Medium / materialization | 54,159 | 685.870 µs / 322,672 B | 2,523.826 µs / 329,806 B | 3.68x | 1.02x |
| Large / complete | 1,075,599 | 14.964 ms / 8,584,485 B | 53.855 ms / 8,732,510 B | 3.60x | 1.02x |
| Large / pipeline | 1,075,599 | 0.369 ms / 2,154,163 B | 0.538 ms / 2,162,148 B | 1.46x | 1.00x |
| Large / adapter | 1,075,599 | 14.541 ms / 6,428,328 B | 55.038 ms / 6,567,542 B | 3.78x | 1.02x |
| Large / materialization | 1,075,599 | 14.879 ms / 6,428,280 B | 55.577 ms / 6,567,651 B | 3.74x | 1.02x |

The response-read row remains the same defect signal on both runtimes: the large body allocates
2,154,163 B (`2.00x` wire) on net10 and 2,162,148 B (`2.01x`) on net472. The fixed downlevel cost is
visible on deep and health bodies, but it amortizes as the body grows; R16 remains an SDK-owned copy
problem rather than a net472-only issue.

**SSE reader, sustained rows (exact framed wire and allocated bytes):**

| Fixture / method | Wire B / items | net10 mean / alloc | net472 mean / alloc | Time ratio | Alloc ratio |
|---|---:|---:|---:|---:|---:|
| Large x64 / whole reads | 140,160 / 64 | 474.098 µs / 329,184 B | 1,366.883 µs / 350,005 B | 2.88x | 1.06x |
| Large x64 / 1,460 B chunks | 140,160 / 64 | 479.495 µs / 329,160 B | 1,381.588 µs / 357,527 B | 2.88x | 1.09x |
| Large x64 / multiline | 141,952 / 64 | 490.732 µs / 324,944 B | 1,355.856 µs / 346,878 B | 2.76x | 1.07x |
| Small x1024 / whole reads | 71,680 / 1,024 | 335.894 µs / 181,880 B | 926.668 µs / 199,250 B | 2.76x | 1.10x |
| Small x1024 / 1,460 B chunks | 71,680 / 1,024 | 339.265 µs / 181,856 B | 948.384 µs / 203,054 B | 2.80x | 1.12x |

This is the first real execution evidence for the downlevel append path: sustained net472 parsing is
2.76-2.88x slower and allocates 1.06-1.12x as much as net10 in the same run. Socket-sized chunks do
not materially change either runtime's mean. The single-frame rows are dominated by fixed buffers:
large x1 was 10.667 µs / 52,416 B on net10 versus 25.409 µs / 72,164 B on net472; small x1 was
1.995 µs / 26,312 B versus 3.723 µs / 43,140 B.

**Limits and decision:** timings compare only the two jobs in this Windows run; no ratio is taken
against the Linux Q121 machine. BenchmarkDotNet flagged multimodal distributions in health adapter/
materialization rows, the medium net472 pipeline row, the large-x64 chunked net472 row, and both net10
single-frame SSE rows. Their point timings are descriptive rather than precision claims; allocation is
the primary cross-run signal, and the sustained runtime gaps remain the decision input. Q123 clears the
net472 execution gate and confirms the existing follow-up order: R18 stage 1 (contract-neutral span
scan), R16 (pooled response-body read, targeting about `1.0x` wire in the read row), then emitter
typed-switch union dispatch using Q121's interface/concrete attribution. Each change keeps its own
same-environment before/after evidence; Q123 itself changes no benchmark or product code.

## Q124: Does decoded-span line scanning remove the SSE reader's per-character bottleneck without changing its contract?

**Method:** the stage-1 change kept the strict UTF-8 decoder, `StringBuilder` frame storage, public
`ServerSentEvent`, and every framing rule, but replaced one `Accept` call per decoded character with
`IndexOfAny('\r', '\n')` over each decoded span and slice appends into the pending line. The existing
reader suite supplied the behavioral comparison: 28 tests across net472/net8/net9/net10 (112
executions) cover leading/interior BOM, CR/LF/CRLF and split CR, split UTF-8 and invalid UTF-8, lines
and frames crossing reads, multiline data, comments/ignored fields, event names, cancellation,
trailing frames, mid-line truncation, and the character limit.

Benchmark baselines came from the untouched `713f09a` code on the Q123 Windows machine. Q123 already
owned the parser default-job baseline; a second clean-copy run captured all 24 SessionLog rows before
the edit (Dry first, default 16:12). The after-change source was mirrored to a new clean directory so
no excluded baseline `bin`/`obj` output survived; Dry completed all 38 parser plus SessionLog cases and
the combined default job completed in 34:58. Both jobs compared net472 and net10.0 in one invocation.

**Parser-only before/after (same machine, exact bytes; speedup = before / after):**

| Fixture / runtime | Before mean / alloc | After mean / alloc | Speedup | Alloc change |
|---|---:|---:|---:|---:|
| Large x1 / net10 | 10.667 µs / 52,416 B | 3.624 µs / 47,216 B | 2.94x | -5,200 B |
| Large x1 / net472 | 25.409 µs / 72,164 B | 6.376 µs / 64,017 B | 3.99x | -8,147 B |
| Large x64 / net10 | 474.098 µs / 329,184 B | 53.184 µs / 323,984 B | 8.91x | -5,200 B |
| Large x64 / net472 | 1,366.883 µs / 350,005 B | 175.724 µs / 341,758 B | 7.78x | -8,247 B |
| Large x64 chunked / net10 | 479.495 µs / 329,160 B | 56.690 µs / 326,376 B | 8.46x | -2,784 B |
| Large x64 chunked / net472 | 1,381.588 µs / 357,527 B | 165.953 µs / 352,298 B | 8.33x | -5,229 B |
| Large x64 multiline / net10 | 490.732 µs / 324,944 B | 56.604 µs / 323,416 B | 8.67x | -1,528 B |
| Large x64 multiline / net472 | 1,355.856 µs / 346,878 B | 172.423 µs / 344,681 B | 7.86x | -2,197 B |
| Small x1 / net10 | 1.995 µs / 26,312 B | 1.688 µs / 26,016 B | 1.18x | -296 B |
| Small x1 / net472 | 3.723 µs / 43,140 B | 2.251 µs / 42,731 B | 1.65x | -409 B |
| Small x1024 / net10 | 335.894 µs / 181,880 B | 85.622 µs / 181,584 B | 3.92x | -296 B |
| Small x1024 / net472 | 926.668 µs / 199,250 B | 261.559 µs / 198,857 B | 3.54x | -393 B |
| Small x1024 chunked / net10 | 339.265 µs / 181,856 B | 84.423 µs / 181,560 B | 4.02x | -296 B |
| Small x1024 chunked / net472 | 948.384 µs / 203,054 B | 303.668 µs / 202,681 B | 3.12x | -373 B |

The fixed allocation reduction comes from appending decoded slices with one capacity decision rather
than growing the line builder one character at a time; no new pool, lifetime, or public buffer contract
was introduced. Socket-sized chunks and multiline framing retain the same order of improvement, so the
result is not an artifact of complete-body reads.

**SessionLog limit:** reader-containing rows generally improved, for example
`created-2048-x64` framing-plus-materialization moved 677.2 -> 432.2 µs on net10 and
1,844.6 -> 1,120.3 µs on net472, while `tool-success-16-x64` moved 1,808.7 -> 1,428.8 µs
on net10. However, the unchanged deserialize-only controls drifted materially between the separate
before and after invocations (including 234.5 -> 340.8 µs for created/net10 and
5,837.8 -> 7,552.8 µs for mixed/net472), and several rows were multimodal. Those runs prove fixture
correctness and no allocation regression, not a precise end-to-end speedup. The direct parser rows are
the performance decision evidence.

**Decision:** retain stage 1. The speedups exceed the machine noise by multiples on every sustained
parser case, all 112 cross-TFM behavior executions pass, allocations do not regress, and no public or
generated surface changed. Stage 2 remains a separate public-lifetime design and is not implied. R16's
pooled response-body read is the next measured increment.

## Q125: Should the remaining performance work continue on the current Pipeline architecture?

**Trigger:** R16 proved its performance premise but exposed a larger design question. The current
`Pipeline` now coordinates owned transport construction, downlevel `ServicePoint` policy, credentials
and request decoration, request serialization, send and redirect classification, timeout budgeting,
one-shot body ownership/decoding, response adaptation, stream opening, SSE cancellation teardown,
frame dispatch, and error mapping. The issue is no longer merely class length: each new lifecycle rule
changes the same orchestration paths.

**R16 experiment (uncommitted):** a clean-copy implementation copied `HttpContent` into an
`ArrayPool<byte>`-backed writable stream and scoped an `EncodedResponseBody` lease across adaptation.
All 1,378 SDK-project test executions passed across net472/net8/net9/net10. Default-job allocation
evidence on the Q123 Windows machine confirmed the mechanism:

| Pipeline row | Before | Experimental | Wire amplification after |
|---|---:|---:|---:|
| net10 deep / medium / large | 6,112 / 110,048 / 2,154,163 B | 1,712 / 1,712 / 1,712 B | 0.78x / 0.03x / effectively fixed |
| net472 deep / medium / large | 10,935 / 124,790 / 2,162,148 B | 5,544 / 5,550 / 1,084,987 B | 2.53x / 0.10x / 1.01x |
| net10 / net472 health | 1,840 / 6,141 B | 1,712 / 5,544 B | fixed cost also decreased |

The complete-operation allocation also fell at every message size. The result is useful evidence, not
an accepted implementation. Review found an important caller-cancellation versus I/O-fault race that
could still map cancellation to `OpenCodeTransportException`, plus a lying `CanWrite` state and an
avoidable facade allocation. The custom buffer, lease, race tests, and benchmark updates remain only
as dirty-worktree evidence and are not committed.

After the architecture-pause handoff was prepared, the dirty experiment also passed the mechanical
repository gate: Slopwatch 0, Release build 0 warnings/errors, whitespace/style clean, and 1,864/1,864
tests across the full Windows matrix. That does not override the review finding: the caller-cancellation
race remains an Important correctness defect, so the experiment is still unaccepted and uncommitted.

**Peer evidence:** Azure.Core centralizes this class of behavior in `ResponseBodyPolicy`: buffered
responses copy the network stream into a `MemoryStream`, cancellation/timeout disposes the source
stream, unbuffered responses may ride a `ReadTimeoutStream`, and response disposal/stream extraction
make ownership explicit. AWS SDK for .NET similarly centralizes invocation and unmarshalling in its
runtime pipeline and gives streaming responses explicit disposal ownership. These are evidence that a
deep runtime pipeline can earn its complexity, not evidence that this SDK should copy their public
surface or adopt pooling by default.

**Decision (maintainer, 2026-08-21):** pause R16, generated typed-switch work, and M4 planning. The
correctness repairs, Windows evidence, Q123, and measured R18 stage 1 remain valid accomplishments.
Before adding another policy to the current orchestration, start a fresh brainstorming session that
evaluates the runtime holistically: internal decomposition versus an internal policy pipeline,
request/response context and ownership, timeout/cancellation budgeting, buffered versus streaming body
strategy, future retry/telemetry/hooks, test seams, performance gates, and migration order. Do not
commit, discard, or build on the dirty R16 experiment until that design decides its fate. M4 remains the
product target after the runtime/performance decision is resolved.

# Session 40 — 2026-08-24: Runtime pipeline architecture design

## Q126: What did the holistic architecture scans and peer-pipeline evidence show?

**Method:** three parallel read-only scans over the Q125 pause state: a very-thorough depth scan of the
hand-written runtime (committed state via `git show HEAD:` for the R16-dirty files), a state scan of the
generator tool, and source reading of Azure.Core (`azure-sdk-for-net` @ `470fcf3`) and AWS SDK for .NET
(`aws-sdk-net` @ `3cd03c5`) runtime pipelines, plus a focused charset follow-up. Candidates were
presented as a temp HTML review (outside the repository) and worked through a grilling session.

**Runtime findings:** committed `Pipeline` (520 lines) is overloaded-deep — eight interface members over
~13 lifecycle policies in two orchestration methods, six shared by copy; the timeout budget straddles the
send; four near-identical failure-classification cascades span two files; the undeclared-2xx verdict has
two authors (stream plane inline, generated adapters' default arm); the SSE framer, body materialization,
transport policy, and clock have no seams, so the internal `(HttpClient, options)` constructor is the
Behavior core's only test seam and ownership tests need four bespoke `HttpContent` doubles. Deletion
tests: `ResponseBodyReader`, `ResponseAdapter`, `IStreamAdapter`, `OpenCodeErrorReader` concentrate and
stay; committed `EncodedResponseBody` is a two-field tuple whose one decision its caller makes.

**Tool findings:** healthy four-stage skeleton with narrow inter-stage seams and no mock pain; the mass
sits in binding — `OperationPlanBinder` (1,319 lines, 24 responsibilities) forces every new operation
shape through four scattered regions of one file, `SchemaPlanBinder` hosts two independent union systems,
three reserved-name tables mirror `src/` facts with no mechanical link, and the 814-line hand-built
`EmitterPlanFixture` pays the same bill in tests. Recorded as an untriggered locality item; not part of
the runtime plan.

**Peer findings:** both SDKs send `ResponseHeadersRead`, detach body lifetime from the response message,
put per-attempt stages inside the retry loop, classify cancel-versus-timeout by inspecting the caller's
token first, and hide runtime mass behind ~55–65-line policy/handler interfaces. They diverge on
buffering (Azure buffers into a plain `MemoryStream` via internal `ResponseBodyPolicy` with a
progress-resetting timeout and dispose-to-interrupt; AWS never buffers), composition (immutable slice-
passing array versus mutable linked handler chain), and context discipline (sealed message with internal
setters versus a wide public bag). The charset follow-up: neither replicates `HttpContent` charset
negotiation — `Encoding.GetEncoding`-on-charset appears nowhere in either core; Azure reads `charset`
only to confirm `utf-8` before printing text it decodes with hardcoded UTF-8; AWS byte-sniffs a UTF-8 BOM
and hands raw bytes to `Utf8JsonReader`. "JSON is UTF-8 bytes" is both SDKs' design axiom; our BCL-parity
breadth is a self-imposed commitment (ADR-0014's consequence sentence), not ecosystem practice.

**Decision (maintainer, 2026-08-24):** proceed to a full runtime design on this evidence; candidates and
the sealed outcome are Q127–Q129 and the plan at `../superpowers/plans/2026-08-24-runtime-pipeline-plan.md`.

## Q127: Which runtime-pipeline architecture is sealed?

**Decision (maintainer, 2026-08-24):** an Azure-style internal policy pipeline now, not a narrow
decomposition and not public extensibility (ADR-0010 untouched). ClientModel-aligned names —
`PipelineMessage` (internal sealed, `IDisposable`, no property bag, pipeline-written members
`internal set`), abstract `PipelinePolicy` with slice-passing `ProcessAsync`, async-only, no mutation
API — with a day-one roster of `RequestDecorationPolicy` → `ResponseBufferingPolicy` → `TransportPolicy`,
so the composition machinery never carries a one-element list. The composed class keeps the `Pipeline`
name and its generated-facing entry points. Post-pipeline, an instance `ResponseMaterializer` owns
decode + verdict consumption + adapter dispatch for both planes (the three duplicated read sites — R12 —
collapse into it).

Status authority is A3-Full: the generator emits `StatusVerdict Classify(int status)` on one-shot and
stream adapters from each operation's pinned status table; `SuccessStatusCode` and `ReadsSuccessBody`
fold into it; planes switch on verdicts only and the undeclared-success message has one author. 3xx
remains `TransportPolicy`'s protocol-invariant refusal because no operation can declare it. Failure
classification centralizes as `FailureClassification.Map(exception, token, phase)` (BCL-derived; the four
cascades become single calls; M6's retryability question gains its home). The stream plane's frame
dispatch moves beside `IStreamAdapter` and is tested with `ServerSentEvent` values; framing arrives
through the named seam `IEventStreamFramer` with `ServerSentEventFramer` as a stateless one-reader-per-
body facade — principle recorded: a seam gets a name, never a delegate parameter.

R16's mechanism is accepted and its dirty code is not: the pooled destination with ownership separated
from the pending copy is re-derived inside `ResponseBufferingPolicy`; buffer lifetime rides
`PipelineMessage.Dispose`; the cancellation race is designed out via the classification map; no
per-operation buffering-strategy selector. `ResponseEncodingPolicy` keeps full `HttpContent` parity on
both planes — ADR-0014's sentence stands — and is rebuilt as a low-allocation, exception-free internal
feature proven by a differential parity matrix (closing R09/R10) plus benchmarks; the pre-parse validity
scan is irreducible under parity and stays. Two standing principles: policy modules declare their
knowledge source (`pin-derived` / `BCL-derived` / `upstream-observed`, the last re-verified at spec
refreshes), and TFM divergence splits by kind (algorithm → per-TFM adapter, API shape → `#if`).

## Q128: Which timeout semantics does the rebuilt pipeline use?

**Decision (maintainer, 2026-08-24):** progress-timeout semantics with Azure's machinery, replacing the
total budget. Each read must progress within `NetworkTimeout` (internal default 100 s); the machinery is
a linked CTS over the caller token, `CancelAfter` re-armed per read inside `ResponseBufferingPolicy`'s
copy loop, and dispose-to-interrupt for uncancellable reads — deleting `Task.WaitAsync` abandonment, the
fault-observation ceremony, and the `Stopwatch` budget arithmetic that straddled the send. The owned
`HttpClient.Timeout` becomes infinite so two mechanisms cannot race. A live SSE success body stays exempt
(existing canon line); every other response — including R17's stream-open error bodies — buffers under
the timer. The `client-runtime.md` timeout paragraph is rewritten and the total-budget tests are replaced
red-first inside Increment 3, not before. A public `NetworkTimeout` knob and an optional total-budget
mode are M6 candidates.

## Q129: What stays deferred or research-gated after the design?

**Decision (maintainer, 2026-08-24):** research doc 17 (modern .NET allocation API sweep, dispatched this
session) gates Increment 3's pool internals, Increment 4 entirely, and the future SSE stage-2; the sealed
structure is not research-sensitive, which is why sealing did not wait for it. A6's
configuration/transport split is deferred with an explicit ROADMAP trigger (M6 transport handlers or a
concrete Extensions `IHttpClientFactory` need). A3's remaining breadth (multi-success operations) needs
no trigger — it lands pre-paid as new `Classify` arms when the binder's fail-closed status wall meets
such an operation. The generator typed-switch optimization stays paused until after the increments; the
B-track binder locality findings are recorded untriggered; M4 planning starts after the increments.
HANDOFF-2026-08-21-12 is consumed and deleted; HANDOFF-2026-08-24-13 carries the operational state.

# Session 41 — 2026-08-24: Increments 1–2 execution and their benchmark evidence

## Q130: What did the Increment 1 → Increment 2 benchmark comparison show?

**Method:** two same-day runs on the same Windows machine as Q123, each from a `robocopy` mirror in
the clean `C:\bench` copy: the baseline at Increment 1's commit `f6b2223`, the after-run at the
Increment 2 worktree that landed unchanged as `910e05d`. Six benchmark families (Health, MessageGet,
NoContent, ErrorChannel, EventStream, SessionLogStream), 36 cases per runtime, `--runtimes net472
net10.0`, `--job Dry` validation before each default job, BenchmarkDotNet 0.15.8. Artifacts:
`C:\bench-artifacts\inc1-baseline-*`, `inc2-after-*`, and the joined `inc2-comparison.csv`.
Environment caveat: development builds ran on the machine during the baseline's default job, so
single-row timings are not comparable — rows with zero allocation delta swing up to ~14x in both
directions; medians and allocation carry the comparison.

**Allocation (exact bytes, the primary axis):** attribution is surgical — every adapter-only and
materialization-only row shows delta 0; deltas appear exclusively on pipeline-touching rows.
net10.0: the one-shot pipeline costs a fixed **+8 B** at every body size (health 1,840 → 1,848 B;
deep 6,112 → 6,120 B; medium +8 B; large within pool noise at +97 B on 2.15 MB), and stream opens
get **−56 B**. net472: the one-shot pipeline gains a fixed **+542..+565 B** (health 6,142 → 6,684 B;
deep 10,933 → 11,496 B) that amortizes with body size (medium −4 B; large +0.08%), while stream rows
move −45..−160 B. Reading: the slice-passing `ValueTask` policy hops box their async state machines
when the downlevel send suspends — the known downlevel price of the sealed ValueTask-hop decision
(doc 17 §5, doc 20 A3); doc 19's `PoolingAsyncValueTaskMethodBuilder` reserve stays benchmark-gated,
and Increment 3 rebuilds the buffered read path where this fixed cost lives, which is the natural
point to re-measure it.

**Timing (secondary, compromised by the caveat above):** median after/before ratio **1.01 on both
runtimes**; the outlier rows (0.09x–14.6x) all carry zero or noise-level allocation deltas and swing
in both directions, which is measurement environment, not code.

**Verdict:** the relocation is allocation-neutral on net10.0 (+8 B fixed, streams cheaper) and adds a
bounded, fixed, downlevel-only ~0.55 KB per one-shot call on net472. Increment 2's behavior
preservation is separately pinned by the unchanged 2,016-test suite passing untouched.

## Q131: What benchmark cadence governs the remaining increments?

**Decision (maintainer, 2026-08-24):** benchmarking is tiered by scope. Increment-level checks are
targeted and short — `--filter` narrowed to the component ladders the change touches, `--job short`,
exact allocation columns as the before/after comparison, timings indicative only — and the full
suite under the default job runs once as milestone evidence when a work arc completes. Sealed into
`quality-gates.md`'s performance section the same day. Recorded alongside: pre-GA the SDK owes no
consumer backward compatibility; the PublicApi lock stays a deliberate-diff review gate, not a
compatibility promise.

## Q132: What did Increment 2b land and what did its targeted benchmark show?

**Landed (commit `2d0c1b1`):** the generator emits `StatusVerdict Classify(int status)` on every
one-shot and stream adapter from the operation's pinned status table; `SuccessStatusCode` and
`ReadsSuccessBody` are deleted; the planes and the materializer switch on verdicts only; the
undeclared-success message has one author (`StatusVerdictFailures`); the stream plane's duplicated
undeclared-2xx author is gone. The noted behavior change shipped with its canon wording: an
unexpected body on a declared no-content success is drained into the buffer and ignored rather than
left unread, which frees the pipeline of operation knowledge (`PipelineMessage` lost its
`NoBodySuccessStatus` member) and keeps buffering one unconditional rule. Doc 20 E5 rode along:
the error reader's comparer-overload `Enumerable.Contains` became an `Array.IndexOf` scan over the
generated tag arrays, whose parameters narrowed to `string[]`.

**Targeted short benchmark (first use of the Q131 cadence; Health/NoContent/ErrorChannel families,
`--job short`, both runtimes, artifacts `C:\bench-artifacts\inc2b-*-short`):** allocation deltas —
error path −8 B end to end on net10.0 and −10..−54 B on net472 with `ReadTolerantError` itself
−32 B on net472 (the E5 win); health pipeline rows −8 B on net10.0 (2,112 → 2,104 B — the
Increment 2 message member given back) and −13..−31 B on net472; the no-content operation pays the
drain's fixed read machinery, +480 B on net10.0 (1,288 → 1,768 B) and +97 B on net472, on an empty
body. The bare `HttpClient` control row moved 0 B on both runtimes. Timings are not quoted from a
short job. Increment 3's pooled read path is where the drain's fixed cost gets rebuilt and
re-measured.

## Q133: What did the Increment 3 entry checkpoint decide?

**Decision (maintainer, 2026-08-24):** the explicit-vs-transitive dependency rule is sealed into
`platform-and-packaging.md` — declare what our source uses directly, what appears on a public
surface, or what is version-pinned for behavior; trust the transitive graph otherwise, with
downlevel bridges conditioned to the frameworks that need them. Its already-due consequence lands
with the seal: explicit `System.Memory` (4.6.3) and `System.Buffers` (4.6.1) on the downlevel legs,
at the versions the transitive graph already resolved. The four package questions: adopt
`Microsoft.Bcl.Memory` when Increment 4 needs downlevel `Utf8.IsValid` (the alternatives keep
exception-based control flow or hand-roll a UTF-8 scanner); neither `Microsoft.Bcl.TimeProvider`
nor an internal clock seam — the sealed progress timeout is `CancelAfter` re-arm with no remaining
clock-reading site, and `CancellationTokenSource` has no TimeProvider hook (doc 19 #8); if M6's
total-budget mode reintroduces deadline arithmetic, `Microsoft.Bcl.TimeProvider` is the abstraction
taken then. `System.Collections.Immutable` defers to the emitter allocation batch with its
benchmarks (the no-package fallback is `#if NET8_0_OR_GREATER` frozen over a plain dictionary
behind one factory), and `System.Net.ServerSentEvents` defers to the SSE stage-2 design per
doc 17 §3.

## Q134: What did Increment 3 land and what does its allocation evidence show?

**Landed (commit `8632408`):** the R16 mechanism re-derived inside `ResponseBufferingPolicy` — the
body copies once into an `ArrayPool` rent sized from the declared length plus the end-of-stream
probe byte, grows by doubling with ownership transferred to `PipelineMessage.Dispose`, and the
`Task.WaitAsync` abandonment, fault-observation ceremony, and `Stopwatch` budget arithmetic are
deleted. Progress-timeout semantics per Q128: one linked CTS per operation spans the send and every
read, `CancelAfter` re-arms on each read that progresses, the owned client's `HttpClient.Timeout` is
infinite, and a live SSE success leaves the policy with the timer dead. `PipelineMessage` gained
`NetworkToken` (the I/O token; classification keeps reading the caller token) and the internal
100 s `NetworkTimeout` default with a test seam. Decisions recorded: the pooled return does not
clear the array (upstream `LimitArrayPoolWriteStream` parity; a response body is not a secret
against its own process); `CancellationToken.UnsafeRegister` carries the dispose-to-interrupt
registration; `GC.AllocateUninitializedArray` found no site — no escaping copy survived the design.
Two mechanism findings: modern `HttpClient.Timeout` never guards a post-headers read (the old test
passed only because the deleted budget arithmetic mirrored it), and `HttpContent.ReadAsStreamAsync`
drops its token before `SerializeToStreamAsync` on the buffering path — so dispose-to-interrupt is
the universal settle guarantee on every target, not a downlevel workaround. Old total-budget tests
were replaced red-first by stall/trickle progress tests plus pooled-ownership tests on the restored
`TrackingByteArrayPool`.

**Targeted short benchmark (Q131 cadence; Health/MessageGet/NoContent/ErrorChannel, both runtimes;
artifacts `C:\bench-artifacts\inc3-*-short`):** the net10.0 pipeline row is flat at every body size
— deep 6,112 → **2,112 B**, medium 110,048 → **2,112 B**, large 2,154,208 → **2,112 B** — and the
complete large call drops 8,583,981 → 6,430,720 B (the wire-size copy gone). net472 fixed costs fall
2.3–2.6 KB per call (health pipeline 6,683 → 4,413 B), repaying Increment 2's ValueTask-hop cost.
The progress machinery costs a fixed +192..+368 B on the smallest net10 bodies. Known limit: the
downlevel `System.Buffers` shared pool caps buckets at 1 MB, so a >1 MB body on net472 still
allocates 1× wire (was 2×); a larger-cap `ArrayPool.Create` on downlevel is a possible follow-up,
benchmark-gated. Adapter and materialization rows moved 0 B. Arc acceptance: the sandbox runs
against a real opencode v2 server after Increment 4, before any push (maintainer, 2026-08-24).

## Q135: What did Increment 4 land and what closed with it?

**Landed (commit `429a08a`):** `ResponseEncodingPolicy` rebuilt exception-free and
allocation-free on its hot path while keeping full `HttpContent` parity (ADR-0014's sentence
untouched): `Utf8.IsValid` replaces the `DecoderFallbackException` round trip (modern inbox;
downlevel through the checkpoint-approved `Microsoft.Bcl.Memory` 10.0.11), the BOM tables are
static span data, the quoted charset strips as a span instead of a substring, a well-known
`utf-8` fast path (`Ascii.EqualsIgnoreCase` on net8+, ordinal-ignore-case spans downlevel)
skips the `Encoding.GetEncoding` lookup, and the double `GetPreamble()` collapsed to one local.
Downlevel deliberately keeps the `(array, index, count)` `Encoding` overloads — the Polyfill
span shims allocate (doc 17 §1). The statelessness rippled: the analyzer wall made both
`ResponseEncodingPolicy` and the field-free `ResponseMaterializer` static classes.

**R09/R10 closed by the differential parity matrix:** sixteen body-and-charset rows assert byte
identity against real `HttpContent.ReadAsStringAsync` on the same target framework — BOM
precedence including UTF-32-over-UTF-16, charset-plus-BOM stripping, malformed-UTF-8 replacement
decoding, empty-body-before-invalid-charset, and matching `InvalidOperationException` refusal —
running on every TFM including the net472 leg. One deliberate-divergence row surfaced: net472's
own `HttpContent` rejects a quoted charset outright, so that row stays differential on modern
frameworks and the dedicated quoted-charset tests pin the repo's modern-everywhere behavior
downlevel.

**Targeted short benchmark (`C:\bench-artifacts\inc4-*-short`):** every valid-UTF-8 row is 0 B
before and after (the hot path stays allocation-free); the malformed-UTF-8 row drops 5,416 →
4,496 B on net10.0 and 6,427 → 4,566 B on net472 (the exception machinery gone; the remaining
bytes are the replacement-decoded string itself); net472's declared-utf8 row drops 24 → 0 B
(the lookup skipped). UTF-16 fallback rows are unchanged — the decoded string dominates them.

## Q136: What did the real-server acceptance run show for the rebuilt runtime?

**Method:** the arc's agreed acceptance (maintainer, 2026-08-24) — the sandbox against a real
opencode v2 server. The installed `opencode.exe` 1.18.21 turned out to be the v1 line and its
`/api/health` body (`{"healthy":true}`, no `version`/`pid`) was refused by the pinned contract's
required-member wall — the fail-closed materialization working as designed, and a clean
demonstration that v1 is not our surface. The real oracle was then run from source at exactly the
pinned commit: `external/opencode` @ `a6a712a3` (bin name `opencode2`), `bun install
--frozen-lockfile` + `bun run src/index.ts serve` on 127.0.0.1:4599, which booted the v2 auth
surface (generated server password, 401 unauthenticated).

**Results (2026-08-25, all through the rebuilt pipeline on net10):** one-shot mode fully green —
health (v2 shape with `version`/`pid` materialized), session create with a JSON body, list with a
live cursor, get, and messages, all through Basic auth, pooled buffering, and status verdicts.
Stream mode opened `v2.session.log`, received the typed `EventLogSynced` frame, and held the live
SSE connection open past 45 s — the buffering exemption behaving against a real socket. Events
mode received `EventServerConnected` on subscribe and dispatched a live `SessionCreated` union
variant triggered mid-stream. Pagination mode ran mechanically but enumerated zero items — the
fresh session has no messages, and producing assistant messages needs a configured provider; a
non-empty enumeration pass stays open for a future session with provider credentials. One sandbox
nit recorded: `--paginate` exits nonzero on an empty enumeration.

## Q137: How far has upstream v2 drifted from the pin, and what would a refresh touch?

**Method (2026-08-25, read-only reconnaissance):** `git fetch origin v2` in the submodule, then a
structural comparison of `packages/protocol/openapi.json` between the pin (`a6a712a3`,
2026-08-13) and the `origin/v2` tip (`71f81dc0fe`, 2026-08-24) — path/operation set diffs plus a
transitive `$ref`-closure comparison for every selected-family operation — and a diff of the
internals on the upstream-observed list.

**Findings:** 633 commits of drift; the document shrank 681 → 478 KB and 324 → 210 schemas, but
the mass decomposes into non-events for this SDK. (1) A wholesale rename: every error schema now
carries an `Encoded` suffix (`InvalidRequestError` → `InvalidRequestErrorEncoded`) and payload
envelopes consolidated under the same convention (`SessionLogItemEncoded`); the wire shape is
byte-identical where checked (`InvalidRequestError` — `_tag`, members, and required set
unchanged). (2) Spec-gen canonicalization: `allOf` constraint wrappers flattened (`ServiceHealth`
semantically identical), and the numeric-suffix collision artifacts our curation fingerprints
(`InvalidRequestError1`, `Shell.Info1`, `Session.Message.ProviderState4/5`, …) are gone —
upstream fixed the duplicate emission, so those curation rows delete at refresh. (3) Real surface
deltas, none in the selected set: the question flow (`v2.session.question.*`,
`v2.question.request.list` — pending question requests a session could ask its caller), the
undocumented experimental `v2.projectCopy.*` trio, and `v2.health.stop` are removed; session
stats/environment/view and worktree operations are added. (4) The dialect is unchanged — OpenAPI
3.1.0, `x-effect-stream` framing metadata with the same `not: {}` never-arm cause schema, `_tag`
unions, `anyOf`-null optionality — and `packages/server/src/location.ts` has zero diff, so the
location-header decoding asymmetry the decoration policy mirrors still holds. Health's status
table is unchanged (200/400/401). A refresh is therefore generator-side work — curation rename
mappings, fingerprint deletions, regeneration, snapshot review — with no runtime mechanism
affected; per `spec/SNAPSHOT.md` it stays deliberate at a milestone boundary.

## Q138: What does the arc-milestone benchmark say about the whole runtime rebuild?

**Method (Q131 cadence):** the full suite — twelve families, 152 cases, both runtimes — under the
default job from a clean `C:\bench` mirror of the final tree (`6891c6e` content), Dry-validated
first; artifacts `C:\bench-artifacts\arc-milestone-{dry,default}` plus `arc-milestone.csv` beside
the earlier extracts. Compared against the Increment 1 default-job baseline
(`inc1-baseline-default`, 72 overlapping cases). Caveat carried from Q130: the baseline ran under
development load while the milestone ran on an idle machine overnight, so timing ratios are
directional; allocation columns are exact.

**Allocation (arc totals, matching the per-increment evidence):** the net10.0 one-shot pipeline
row is flat at 2,112 B at every body size (deep −4,000 B, medium −107,936 B, large −2,151,966 B);
complete large calls drop ~2.15 MB (net10.0) and ~1.08 MB (net472) per operation; net472 fixed
costs fall 1.7–2.1 KB per call. The bounded regressions are the progress machinery on the
smallest bodies (+192..+272 B net10.0), the stream-open path (+248 B net10.0, +6xx B net472), and
the no-content drain (+848/+803 B).

**Timing (directional):** downlevel one-shot rows land at 0.35–0.55× the baseline (the WaitAsync
abandonment and double-buffering machinery gone), net10.0 pipeline rows at 0.25–0.78× with the
large-body row 4× faster; materialization-dominated rows sit at parity (0.97–1.03×), as they
should — the arc never touched them. The one timing regression is the no-content operation
(2.27× net10.0, still sub-microsecond absolute), the measured price of draining under a timer.
The arc's quotable summary: body-size-proportional allocation is gone from the pipeline, downlevel
calls are roughly twice as fast, and every cost added is fixed, small, and named.

# Session 42 — 2026-08-25: spec-refresh attempt, upstream SSE payload regression, issue #44911

## Q139: Why did the spec refresh stop, and is the lost stream surface recoverable upstream?

**Method:** executed the `spec/SNAPSHOT.md` refresh procedure toward the `origin/v2` tip
`8c126e98da` — Q137's mapped `71f81dc0fe` plus a 48-commit tail verified hunk-by-hunk to sit
entirely outside the selected surface (`session.interrupt` response shape, `generate.text`
location-parameter removal, new `workspace.*`/pty operations; `location.ts` zero diff). The
generator's ingestion wall refused the new document with five errors; the refusal was
root-caused through a submodule bisect, the effect changelog, and effect rc.111 source, then
tested end to end in a scratch worktree of the submodule at the tip (`bun install
--frozen-lockfile --ignore-scripts`; `bun run check:generated` first reproduced the committed
document byte-identically, proving the harness).

**Findings — the regression:** the wall is right. Since upstream `aca42423d3` (2026-08-17,
`chore: generate` — the first regen under effect beta.107; its parent `d9b81d2233` is the
`beta.101 → beta.107` bump itself, with `script/generate-openapi.ts` unchanged), the generated
document no longer declares the SSE payload schemas. The `*JsonString` envelopes
(`{type: string, contentSchema: {$ref: union}, contentMediaType}`) became bare `*Encoded`
strings (`{type: string, contentMediaType}`), and with the `contentSchema` link gone the entire
event model tree — `V2Event`, `SessionLogItem`, every leaf; 74 components, 314 → 240 in that one
regen — was pruned as unreachable. The wire protocol is unchanged; this is a documentation-only
regression. The cause chain sits in effect: beta.102
[#6424](https://github.com/Effect-TS/effect/pull/6424) (encoded-side representation;
`contentSchema` demoted from structural field to the `Annotations.Augment.contentSchema`
annotation, which nothing in `HttpApiSchema.StreamSse`'s OpenAPI projection re-attaches),
beta.103 [#6781](https://github.com/Effect-TS/effect/pull/6781) (prune components unreachable
from a generated root), and cosmetically beta.103
[#6782](https://github.com/Effect-TS/effect/pull/6782) (`Encoded` suffix naming). Upstream's own
TypeScript clients generate from the effect schemas directly, so nothing broke in-repo and the
regression went unnoticed there.

**This supersedes Q137's stream-channel claim.** Q137's "payload envelopes consolidated under the
same convention … no dialect or runtime impact" was wrong for the stream payload channel: a
refresh onto any commit at or after `aca42423d3` erases the protocol source of the typed stream
surface, exactly the projection-loss class the OpenAPI-projection-fidelity open question
anticipated (report upstream, never repair through curation — ADR-0013). Q137's one-shot surface
map, dialect findings, and location-asymmetry verification remain valid.

**Findings — recoverability:** the road back is open at the tip (effect `4.0.0-rc.111`).
`Schema.toJsonSchemaDocument(OpenCodeEvent)` still compiles the complete union (138 definitions,
root `$ref: V2Event`); the JSON Schema emitter still treats `contentSchema` as a first-class
annotation key and emits it when present; merging the compiled definitions into
`components.schemas` and re-attaching `contentSchema: {$ref: V2Event}` on `V2EventEncoded`
restores the exact pre-regression envelope shape. Of 46 compiled schemas already present in the
document, 45 are byte-identical; the restored union carries the current 85 variants (including
post-regression `session.viewed` and `worktree.*`), and the published snippet was validated
verbatim — every one of the 953 component `$ref`s in the resulting document resolves. The
restore is a ~30-line post-processing step in upstream's own generate script.

**Decision (maintainer, 2026-08-25):** refresh deferred; the pin stays at `a6a712a3`. The
regression was reported upstream with the diagnosis, the verified restore recipe, and a PR offer
as [anomalyco/opencode#44911](https://github.com/anomalyco/opencode/issues/44911) (deliberately
free of social-media references). The repository was made public the same day. The emitter
allocation batch proceeds on the current base. Carried for the next sanctioned refresh:
`v2.pty.connect.token` introduces a `header` parameter (`x-opencode-ticket`) the ingestion wall
refuses — an admit-or-exclude decision rides whichever refresh lands first.

# Session 43 — 2026-08-25: emitter allocation batch entry — records tooling, increment 1, sealed decisions

## Q140: What did the emitter allocation batch's entry land, and which doc 20 findings were already closed?

**Records tooling (landed `07c772c`):** `opencode-tool compare-benchmarks <before> <after>
[--output <csv>]` replaces the session-scratch extract scripts. It reads each run folder's
`results/*-report-full.json` exports, joins cases on (FullName, runtime leg) — runtime parsed
from `DisplayInfo`'s `Runtime=` token with a job-name fallback — reports exact
`BytesAllocatedPerOperation` deltas beside an indicative median ratio, lists one-sided cases
instead of dropping them, and writes the established
`"Case","Runtime","AllocBefore","AllocAfter","AllocDelta","TimeRatio"` extract (invariant
culture). Fail-closed on missing directories, missing exports, missing MemoryDiagnoser data,
duplicate cases, and zero overlap. Verified by reproducing Q138's 72-case arc-milestone
comparison from the seeded `.benchmarks/` store byte-for-byte on the quoted rows.

**Batch order (maintainer, 2026-08-25):** (1) decision-free small findings, (2) doc 20 #8
`FrozenDictionary` tag tables, (3) #3 net9+ alternate span lookup, (4) #10 route/query churn,
measurement-gated. Sealed alongside: **`System.Collections.Immutable` is adopted as a
downlevel-conditional package** — only the TFMs without in-box `System.Collections.Frozen`
(net472/netstandard2.0) take the reference, following the Q133 downlevel-bridge pattern; modern
TFMs use the in-box types. The canonical `platform-and-packaging.md` sentence rides increment 2
with maintainer review.

**Polyfill finding (evidence for #3's net9+ gate):** Polyfill 11.0.2 (the pin) does ship
`GetAlternateLookup`/`DictionaryAlternateLookup` for downlevel TFMs, but its own remarks declare
the mechanism: O(n) linear key scans via `IAlternateEqualityComparer.Equals` per entry, versus
the native .NET 9+ O(1) hash-bucket lookup (`TryFindKey` iterates `dictionary.Keys`). Against
40–87-entry stream-union tag tables that trades a ~24–60 B per-payload string for a full table
scan per dispatch — a CPU regression on the hottest path, and the polyfill targets `Dictionary`,
not the frozen tables increment 2 introduces. Doc 14's inventory predates this API; recorded
here rather than there. Consequence: generated span-lookup dispatch stays `#if NET9_0_OR_GREATER`;
downlevel keeps string-key O(1) lookups and continues paying the tag-string allocation — a known,
named boundary.

**Already closed by the runtime arc (verified against current source):** doc 20 D2 (#7) — the
location-directory escape is hoisted to construction in `RequestDecorationPolicy` (`:31`), exactly
the "fold into increment-2's RequestDecoration policy" disposition; and D3 (#9) — one shared
`static readonly MediaTypeHeaderValue JsonMediaType` lives in `Pipeline` (`:26`) under the
documented no-mutation discipline. Neither needed batch work.

**Increment 1 (#12 / doc 20 D5):** the emitter now caches one static empty request instance per
distinct optional body type on the owning client (`EmptySessionCreateRequest`) and the omitted-body
coalesce targets it; generated request records are immutable, so sharing is safe. Regeneration
touches only `SessionsClient.cs` (one field, one expression); manifest membership and the
PublicApi baseline are unchanged (private field). No benchmark family exercises the omitted-body
create path, so no numbers are quoted: the claim is the removed per-call record construction,
visible in the diff. All base gates plus the tool smoke and `generate --verify` ran green
(2,182 tests, 0 failed/skipped).

## Q141: What did the FrozenDictionary increment land and what did its targeted benchmark show?

**Landed (doc 20 #8):** every generated union tag table — 15 converters — now builds its
`Dictionary` literal once and freezes it: string-marker tables call
`.ToFrozenDictionary(StringComparer.Ordinal)` (the comparer rides along because
`ToFrozenDictionary` does not inherit the source's), numeric-marker tables freeze with the
default comparer, and the field type is `FrozenDictionary`. The converter emitter adds the
`System.Collections.Frozen` using. `System.Collections.Immutable` 10.0.11 enters
`Directory.Packages.props` and is conditioned in `OpenCode.Sdk.csproj` to
net472/netstandard2.0 only — the Q133 downlevel-bridge pattern; net8.0+ uses the inbox types
(decision sealed in Q140). The five-TFM Release build proves downlevel resolution, the union
emitter micro-snapshot pins the new table shape, and manifest membership plus the PublicApi
baseline are unchanged.

**Targeted short benchmark (Q131 cadence):** the five touched families —
EventStream, SessionLogStream, UnionDeserialization, MessageGet, ErrorChannel — ran as 86
cases under `--job short` on both runtimes from a clean mirror, before at `a88c445` and after
with only this increment applied; artifacts `.benchmarks/frozen-{before,after}-short`, joined
by `compare-benchmarks` into `frozen-comparison.csv`. **Allocation (the evidence axis): every
net10.0 row shows delta 0**; the only nonzero rows are net472 large-allocation cases within
±0.01% (largest −499 B against 6.57 MB), measurement jitter — matching the expectation that
freezing moves cost into one-time static construction and never into the per-payload path.
Timing (indicative only at this tier, never quoted as evidence): after/before ratios span
0.61–1.61 with mean 1.09; the dispatch-densest case (1,024-frame `DeserializeConcreteFrames`)
leans faster (0.85 net10.0 / 0.98 net472) while several small-payload rows lean slower on both
runtimes, uncorrelated with dispatch density — short-job noise. The #8 timing verdict rides the
batch-closing default-job milestone comparison.

## Q142: What did the alternate span-lookup increment land and what did its targeted benchmark show?

**Landed (doc 20 #3):** `UnionDiscriminatorReader.ReadString` is replaced by the generic
`TryFindKnown`, which finds the marker with the duplicate-marker last-wins semantics unchanged,
refuses non-string and whitespace markers exactly as before, and on net9.0+ copies the marker
into a 128-char stack buffer (`CopyString`) and dispatches through
`FrozenDictionary.GetAlternateLookup<ReadOnlySpan<char>>` — a known tag never materializes its
string. The unknown path still allocates the marker because the carrier preserves the wire tag,
and a marker longer than 128 UTF-8 bytes (necessarily unknown to our tables) takes the string
path. net8.0 and the downlevel targets keep the string-key lookup — the boundary named by Q140's
Polyfill O(n) finding. String-marker converters now emit one combined
`if (DiscriminatorReader.TryFindKnown(...))` dispatch; boolean-marker emission is unchanged, and
the emitter fails loudly if a string marker reaches the old reader path. A new runtime fixture
pins the >128-byte unknown marker through a generated converter on all four test TFMs
(net472/net8/net9/net10 — both `#if` branches execute in the suite).

**Targeted short benchmark (Q131 cadence; the same five families, 86 cases, before =
`frozen-after-short` at `7af1fac`, after = `alternate-after-short`, joined into
`alternate-comparison.csv`):** the deltas are surgical. net10.0: every union-dispatching row lost
exactly its tag strings — −49,152 B on the 1,024-frame EventStream and SessionLogStream rows
(48 B × 1,024 tags), −168,040 B on large-message materialization, −8,440 B on medium, −320 B on
the deep fixture, −64 B per error-channel read — while `DeserializeConcreteFrames` (typed
directly, no dispatch) and every non-union row moved 0 B, attributing the change completely.
net472 deltas stay within jitter (largest +416 B against 6.57 MB), the expected unchanged string
path. Timings remain indicative at this tier; net10.0 ratios lean favorable (0.62–1.15) and the
verdict rides the batch-closing default-job run.

## Q143: Is route/query composition worth an emitter rework?

**Method:** a new permanent component rung, `RouteCompositionBenchmarks`, isolates composition
per request shape — the constant route as the composition-free control, the two-parameter path
route, and the query-bearing list route (limit + order + cursor) — Dry-validated first, then
`--job short` on both runtimes; artifacts `.benchmarks/route-{dry,short}`.

**Numbers (exact allocation, indicative timing):** constant 0 B at ~0 ns on both runtimes; path
184 B / 185 B allocating at 77 ns / 589 ns (net10.0 / net472); full query 824 B / 850 B at
262 ns / 1,006 ns.

**Verdict (doc 20 D1's own gate):** not visible next to materialization. The deep one-shot
materializes 12,704 B and a medium list page 314 KB, so composition is at most ~1.2% of the
smallest parameterized call and far below any list-call payload. Honest negative: the D1 fix
sketches (pre-escaped names, presized or struct builder, `string.Create` routes) stay unacted,
and the rung stays in the suite as the standing gate for revisiting. This closes research doc
20's ranked emitter rows: #12 (Q140), #8 (Q141), #3 (Q142), #10 (this entry); D2/D3 (#7/#9) had
already landed inside the runtime arc's RequestDecorationPolicy and shared JsonMediaType. The
batch's remaining evidence step is the closing default-job comparison against
`arc-milestone-default`, which also carries #8's timing verdict.

**Q140 measurement addendum (2026-08-25, later the same day):** the omitted-body path is now
measured instead of argued from the diff. `SessionCreateBenchmarks` gained a permanent
`CreateSessionOmittedBodyAsync` row (the parameterless call that takes the
`request ?? EmptySessionCreateRequest` branch; the body-carrying row and `SerializeRequest`
stay as controls). Isolated legs — before at `07c772c`, after at `a88c445`, the identical
benchmark file overlaid on both worktrees — Dry-validated then `--job short` on both runtimes,
artifacts `.benchmarks/empty-request-{dry,before-short,after-short}` joined into
`empty-request-comparison.csv`: the omitted-body row drops **−56 B (net10.0) / −57 B (net472)**
— exactly one `SessionCreateRequest` record — while both control rows move 0 B on both runtimes.

## Q144: Which walls actually stand between the current profile and the full pinned surface?

**Method (wall-probe spike, zero worktree mutation):** a scratchpad file-based app compiled with
`AssemblyName=OpenCode.Sdk.Tools.Tests` (the tools assembly's unsigned `InternalsVisibleTo`
grants internals access) builds the production container through `ToolApp.CreateServices` minus
the logger providers, ingests the pin once, and per candidate binds the current 15-operation
profile plus that one candidate, then runs `SourceEmitter.Emit` — pure in-memory, the writer is
never invoked. `BindingException` carries every collected `(Category, Subject, Problem)` error,
so one pass yields each operation's complete refusal set. Round 2 unmasks the walls hiding
behind missing curation by synthesizing a `groups` row per uncovered family (client placement;
handle placement when the operation's path carries parameters) through a patched temporary
curation document. Round 3 binds all individually-green candidates together. The probe also
discharges doc 18 §8's gate: the drift check found zero commits touching `Generator/Binding`
since baseline `05cd5d7` (only Emission moved — `2d0c1b1`'s status verdicts and the Q140–Q143
emitter batch), so the scan's findings stand unrevised and the cost model runs on this
inventory.

**Numbers:** 120 operations in pin `a6a712a3` − 15 selected − 6 removed upstream per the
Q137/Q139 drift map (`health.stop`, `project.directories`, `question.request.list`, three
`projectCopy.*`) = 99 candidates. Round 1 (repo curation as-is): 20/99 bind and emit — all
`session.*`, riding the existing group row. Round 2 (synthesized rows): **52/99** — 32
operations' only blocker was the routine curation row. Round 3: 51/52 bind and emit
**together**; the single cross-operation collision is `v2.provider.get`'s derived
`ProviderRequest` colliding with another generated type — one naming-curation row. The 52 span
19 families: session 20, integration 6, mcp 3, pty 3, five two-operation families (agent,
credential, permission, provider, websearch), and ten singletons.

**The 47 refusals partition exactly by primary wall:** bodyless POST (12 — `mcp.connect`,
`mcp.disconnect`, `pty.connect.token`, `session.background`, `session.form.cancel`,
`session.inbox.queue`, `session.inbox.steer`, `session.interrupt`, `session.question.reject`,
`session.revert.clear`, `session.revert.commit`, `session.wait`; the emitter-side empty-request
infrastructure from Q140 already exists); inline nominal schema promotion (11 — `config.get`,
`form.request.list`, four `integration.*`, five `session.form.*`; the same operations' naming
collisions share this root, e.g. `ConfigModelCost` against `Config.Model#/properties/cost`);
success payload not a named schema (+4 unique — `project.list`, `server.get`,
`debug.location.list`, `experimental.migration.v1.status`); location envelope not a named
reference (+2 — `model.default`, `shell.output`, whose real gap is the inline `data` object:
its cursor/limit queries are numeric-pattern strings in the pin, so the earlier "integer cursor
query" note was response-side); list envelope payload not a required named reference (+7 —
`session.active`, `session.context`, `session.inbox.list`, `session.permission.list`,
`session.question.list`, `session.instructions.entry.list`, `permission.saved.list`); PUT
unsupported (3 — `mcp.add`, `pty.update`, `session.instructions.entry.put`); required or
non-null query walls (+2 — `fs.find`, `vcs.diff`); naive-pluralization refusals (2 — `fs.list`,
`pty.list`, routine curated names); payload/response-spine collisions (2 — `location.get`,
`vcs.status`, doc 18 B2's territory); singletons (3 — `config.get`'s structural-union branch
overlaps in `Config.Info`'s lsp/references maps, `fs.read`'s wildcard path, and `pty.connect`'s
WebSocket upgrade, an ADR-0008 exclusion-fingerprint candidate). `pty.connect.token` today
fails only the bodyless-POST wall; its `x-opencode-ticket` header parameter arrives only with
the blocked refresh (#56).

**Decisions (maintainer, 2026-08-25):** the full-surface breadth push proceeds. The 52 routine
operations land as family-grouped A-batches with the structure delegated to the session: A-1
the session twenty on the existing group row, with doc 18 B2 — the single reserved-name owner —
riding this first generator-touching increment; A-2 integration + mcp + pty; A-3 the five
pairs, carrying the `ProviderRequest` naming row; A-4 the singletons. Every increment lands its
curation rows, regenerated source, scaffold-driven contract tests, and the Extensions
registration roster for new client families together, through the full local gate chain. B1
(facet binders) lands before the mechanism batches — bodyless POST plus PUT, inline promotion
(design-first), envelope extensions, and the query walls — exactly the new-shape work doc 18's
cost model says justifies it; B3/B4 stay sequenced behind B1.

## Q145: What did the A-series land, and which surface decisions sealed it?

**Landed (all four batches, 52 operations, profile 15 → 67 selected / 53 pending):** A-1 the
session twenty (`faef68b`) with the first request-side union (fork's tagged boundary through the
generated token-dispatched converter), the omitted-body branch on compact, and eight error
types; A-2 the Integrations/McpServers/Ptys families (`31b3d11`) with three new tagged unions
and the curated MCP-server names; A-3 the pair families (`6bb5813`); A-4 the singletons
(`f573bfb`). Every batch ran the full local gate chain with additive-only PublicApi reviews
(zero removals across all four); the suite grew 2,186 → 2,269 tests. The committed sandbox's
new session-actions walkthrough ran live against the pinned server (`c02c0e8`): export,
permission create with a typed `Deny` effect, compact's queued inbox item, and fork accepting
the serialized `{"type":"through"}` boundary on the wire, with NoThrow carrying a typed 404.
The permission lifecycle closed by curl with an `OPENCODE_CONFIG_CONTENT` probe agent:
create → `ask` → GET 200 pending → reply 204 → GET 404 (consumed) — the earlier 404s were the
deny path (an agentless session resolves the deny-all fallback ruleset; only `ask` parks a
pending request), byte-identical between the SDK and curl.

**Decisions (maintainer, 2026-08-25):** ADR-0019 — handle clients only for working objects,
judged against the complete pinned surface (the mcp near-miss is the recorded motivation);
Agents/Credentials/Permissions/Providers flattened to id-argument methods before commit, the
pushed handle families stood. The ADR is anchored to the Azure SDK .NET guidelines with exact
anchors; the one unadopted guideline (`dotnet-subclient-properties`, the id as a property on
the handle client) joins the parked freeze-time surface review. Group curation rows now carry
a **mandatory reason** (validator wall + red test): every placement documents itself where it
stands. Two structural catches: the `Models` family name refused by the writer's shadow wall
(the provider catalog is `LanguageModels`), and the stock `[Dd]ebug/` gitignore pattern
silently swallowing the Debug family's source until a narrow negation admitted it (`ccd2a9f`)
— both feed the maintainer's parked folder-layout review. Naming principles applied through
reasoned rows: verb-owning operations shed transport prefixes (`GenerateTextAsync`,
`EvictLocationAsync`, `UpdateCredentialAsync`, `QueryAsync`), list subjects pluralize, and
mass-noun containers cover the groups that do not (`Debug`, `Experimental`, `Generation`,
`Vcs`, `Websearch`). CI note: billing refilled mid-session — `9da0ae3` green on rerun, every
push since verified on the hosted three-OS matrix.

## Q146: What did the B-1 mechanism batch land, and what stood behind the walls?

**Method:** B1's facet-binder refactor landed first (`f613866`, behavior-preserving,
`generate --verify` byte-identical), exactly as the doc 18 gate sealed. The batch itself opened
with Q144's zero-mutation probe rerun scoped to the fifteen candidates against the relaxed
wire-shape wall: all fifteen bind and emit green — individually and together — so nothing
hides behind the primary bodyless-POST and PUT refusals and the batch boundary is exactly the
Q144 partition.

**The mechanism:** one wall rule in `OperationWireShapeWall`. POST now admits both body
shapes (the pin demonstrates bodyless POST across twelve operations; the spec is the
authority), `put` joins the admitted verbs, and PATCH plus PUT keep the body requirement
fail-closed under the unchanged message template. Emission needed zero changes: a bodyless
POST rides the same no-content `ExecuteAsync` call a GET emits, and PUT rides the BCL
`HttpMethod.Put` on every target (only PATCH needs the internal `OpenCodeHttpMethod` spine).
The binder's unsupported-method refusal became unreachable through ingestion — the two
allow-lists are now identical — so its red test was removed; `PathItemWallPolicy` owns that
coverage (`HostWallPolicyTests`), and the binder wall stays as defense in depth.

**Landed:** the fifteen operations (twelve bodyless POSTs including `mcp.connect`/`disconnect`
and the session action ops, three PUTs — `mcp.add`, `pty.update`,
`session.instructions.entry.put`); the profile moves 67 selected / 53 pending → **82 / 38**.
Four error types join `IOpenCodeError` (`ForbiddenError`, `FormAlreadySettledError`,
`FormNotFoundError`, `InstructionEntryValueTooLargeError`). Two hygiene catches: the pinned
no-`V2` golden refused `pty.update`'s promoted inline `size` member (`V2PtyUpdateSize`), fixed
with a reasoned schema-name row (`PtyUpdateSize`); and the CA1056 arbitration glob gained
`McpOAuthConfig`/`McpRemoteConfig`, whose wire members the pin declares as plain strings.
`pty.connect.token` is admitted today on the pinned surface; its `x-opencode-ticket` header
parameter arrives only with the blocked refresh (#56) and will announce itself at the wall.

**Evidence:** binder scenario red/green (bodyless POST binds with a null body slot, PUT binds,
bodyless PUT and PATCH stay refused); seven contract tests covering the new wire shapes — the
bodiless POST sends no content (`Body`/`ContentType` both null), PUT carries the verb and the
typed body, the omitted optional PUT body sends the cached `{}`, and the 409/413 paths
materialize the new error types; the PublicApi review was additive-only (+336 lines, zero
removals, all four TFMs byte-identical). The suite grew 2,269 → 2,298 through the full local
gate chain with the tool smoke and `generate --verify` current at 82/38. The committed
sandbox's mechanism leg then ran the batch live against the pinned server (bun-launched from
`external/opencode`): interrupt and revert-clear answered 204 to genuinely bodiless POSTs,
`mcp.add` accepted the `IMcp` union config over PUT, `pty.update`'s PUT round-tripped with the
server echoing the renamed title, the instructions entry PUT/remove cycled 204, and
`form.cancel` on a missing form carried a typed `FormNotFoundError` over the NoThrow spine.

## Q147: What does the OpenAPI document fail to carry, at the pin and at the tip?

**Method:** a git worktree of the submodule at the `v2` tip with `bun install --frozen-lockfile`,
upstream's own generator, the #44911 restore step executed for real, our generator run against
the result, and a four-way parallel source audit of the Effect contract, the schema layer, the
server package, and the per-endpoint channels. The submodule checkout never left the pin. Full
findings, the two reference points, and the choice space: `21-openapi-projection-fidelity.md`.

**Headline:** endpoint parity is exact (131 contract endpoints, 131 operations, zero drift in
both directions, mechanism verified) and declared channels project faithfully (zero parameter or
status mismatches). The loss is in type information and in server behaviour the contract never
declares.

**Closed questions:** the duplicated `Form.*` generation is an upstream artifact — verified by
reading the pinned source, where every `Form.*` identifier is defined exactly once and the
`form.created` event reuses the same `Info` object — and the tip has already converged it, because
the effect upgrade that broke the streams also unified the divergent `Schema.Number` rendering
that produced it. Running the restore step against the tip leaves the `Form.*` name set
byte-identical, with one benign conflict. So the refresh does resolve the form class; the earlier
claim to that effect was correct but had been asserted from inference, and is now measured.

**Our defects surfaced:** the SDK has no per-request header channel, which makes the B-1 batch's
`PostConnectTokenAsync` unusable (live: 403 without `x-opencode-ticket: "1"`, 200 with it) and
also blocks the `x-opencode-directory` multi-project targeting recorded in doc 01 §4; the envelope
binder accepts only `data: $ref`, which is 18 of 31 refusals at the restored tip; error unions are
not deduplicated (47 operations); the "inline nominal schema was not promoted" diagnostic actually
fires on a missing *name*, which is what misframed an entire batch; and the alias guard cannot
separate semantically distinct types that project identically (`Money.USD` vs
`Money.USDPerMillionTokens`).

**The refresh's price and prize:** at the restored tip our generator ingests 123 operations, binds
92, refuses 31 — and refuses none of what we ship today, losing only the two question operations
upstream deleted. Three new ingestion walls arrive with it (eight `persistentPty.*` operationIds
without the `v2.` prefix, a base64 `contentEncoding` shape, the `x-opencode-ticket` header). The
document also asserts `security: []` on all 131 operations while the server requires
authentication — the same class of gap as #44911, and worth reporting.

## Q148: What did the coverage-program grilling seal, and what did its fact-finding measure?

**Method:** the maintainer-approved program design
(`superpowers/specs/2026-08-26-continuous-protocol-coverage-program-design.md`) went through a
full design-tree grilling (2026-08-26); four read-only lookups grounded the contested branches —
the `serve` process contract at the pin, the simulation backend, persistentPty's marking at a tip
worktree, and the server's location resolution. The design document was amended in place where a
lookup contradicted it.

**Process truth (pin):** `serve --stdio --port 0` exists (the reference client's exact argv);
readiness is one JSON stdout line `{"url"}` printed only after full boot; stdin is the ownership
lease (EOF → scoped teardown; upstream regression-tests it by SIGKILLing the owner); auth is
always-on HTTP Basic with hard-coded user `opencode`, `/api/health` included. In stdio mode the
password is never printed and the env copy is scrubbed, so the caller must generate the credential
and inject `OPENCODE_PASSWORD` at spawn — the design's readiness-supplies-credential sentence was
backwards and is corrected. Reference teardown: SIGTERM, 3-second force-kill, group kill
(`taskkill /T /F` on Windows). No JWT and no second auth system exist; the PTY connect flow mints
a short-lived query-carried value through the normally-authenticated token endpoint because a
browser WebSocket upgrade cannot carry the Basic header.

**Simulation (pin):** `OPENCODE_SIMULATE=1` plus `OPENCODE_DRIVE=1` (both required) plus a
provider block via `OPENCODE_CONFIG_CONTENT` switch the server's HTTP client to a deny-by-default
route table; a WebSocket JSON-RPC controller scripts chunked completions while everything below
the response bytes is the real pipeline (SSE decode, session runner, Bus events, SQLite).
Constraints: only the bun-built/source-run server bundles the package (the Node build excludes
it); the control endpoint needs explicit ports (`DRIVE_REGISTRY_DIR` manifest); an unattached
controller hangs prompting; scripted tool calls execute real tools unless synthetic tools are
registered. Decision: a repository-owned C# controller in the shared test infrastructure.

**persistentPty (tip):** no machine-readable stability flag exists — the "experimental" status is
the `server.experimental` group id (whose leak into 8 of 9 operationIds *is* doc 21's T3), the
over-selecting `/api/experimental/` path prefix, and a `"Prototype persistent PTY routes."` tag
description; `v2.persistentPty.connect` breaks every pattern (`x-websocket`, excluded by
upstream's own clients). Upstream's production CLI already calls `persistentPty.shutdown`,
violating the design's no-first-party-consumer criterion — so ExperimentalDeferred was dropped
entirely and persistentPty is ordinary target surface: the eight HTTP operations land as a normal
batch after the refresh, the WebSocket door after the normal-PTY session machinery, with
daemon-gated 503 exemptions where CI lacks `opencode-pty`.

**Location (pin = tip, byte-identical resolver):** the server resolves each member independently —
directory: query → percent-decoded header → cwd; workspace: query → raw header → unset. Upstream
clients have no client-level location and no merge code anywhere; `/api/session/{id}/*`
middlewares ignore location inputs entirely (session-row derived), with `form/global` the one
header-driven escape hatch. Sealed: member-by-member client-side merge between per-call and
ambient (per-call wins, null inherits, no per-call clearing), uniform header injection with the
session-route no-op documented, encoding asymmetry mirrored (directory percent-encoded, workspace
raw and omitted when absent). Naming trap for tests: the query member is `workspace`; the
`session.create`/`import` body member is `workspaceID`.

**Decision register (maintainer, 2026-08-26):** accepted-snapshot vocabulary replaces "spec pin"
(recipe/receipt/normalized defined; admission states Selected/Pending/TransportOwned; the
operation inventory subsumes `generation-profile.txt`); Restore is the only snapshot patch class
and identity defects ride operation-identity curation rows instead (ADR-0013/0020) — the design's
Stabilize class was dropped; refresh cadence is per-session once prepare/verify/apply exists;
every observation lane runs upstream from git source at resolved SHAs (pinned bun, install
scripts disabled, no npm artifacts) — the canary's install-channel identity was replaced
accordingly; location per the design's §5 with #37 closed; declared-nullable envelope payloads
materialize as typed null successes while the non-nullable `{"data":null}` refusal stands; normal
PTY's public family is hand-written over generated internals (ADR-0021; split-ownership partials
rejected); deterministic evidence gates releases (ADR-0022); the assurance ledger is
hand-authored under `tests/` with an `opencode-tool` verifier; M4 = launcher/fixture plus the
simulated-model session workflow, M5 = surface and inventory opening with the first patched
refresh, M6 = automation, canary, and patch retirement. The SSE restore was sent upstream and is
open ([anomalyco/opencode#45182](https://github.com/anomalyco/opencode/pull/45182),
`needs:issue`). ADR shape per `docs/adr/README.md`: three new records (ADR-0020/0021/0022), four
in-place revisions (0003 relay, 0007, 0008, 0013), relay touches to 0005 and `spec/SNAPSHOT.md`;
coverage-ledger and assurance-lane mechanics are conventions that land with their implementing
increments.

## Q149: Do doc 21's findings survive at the current tip?

**Method:** the doc 21 apparatus rerun on 2026-08-26/27 against tip `6170221e2189` (98 commits
past the `a5829431b0` re-check), calibrated first by reproducing doc 21's TIP numbers exactly on
the baseline document; the submodule checkout never left the pin.

**Headline:** everything survives in substance. `contentSchema` is still 0 — the #56 unblock
criterion is not met and the refresh remains blocked solely on the SSE regression. The restore
step still applies cleanly (TIP+RESTORE 316 → 326 components). Operations 131 → 133: upstream
added `v2.session.messageUpdate` (PATCH) and `v2.credential.activate` (POST), and both bind green
through our generator — the probe moves 123/92/31 → 125/94/31 with a byte-identical refused set
and refusal-class histogram. T2 (`security: []` everywhere) and T3 (the same eight off-convention
operationIds) persist; the `Form.*` family stays converged; upstream's committed document is
still stale against its own generator, now missing four member-level drifts
(`Integration.metadata`, `Model.requireReasoning`, the `Project.Vcs` enum→pattern change, and
`session.step.streamed`). One correction to doc 21 §4's table: the
`DELETE-with-body` refusal (`v2.worktree.remove`) exists at the baseline too — it was absorbed by
the ten-class regrouping, not introduced by the tip. Artifacts:
`.scratchpad/openapi-v2-tip-6170221e*.json`, `.scratchpad/measure-doc.ts`; the oc-restore
worktree sits detached at `6170221e`.

## Q150: What did the first receipt-governed refresh land, and what did its walls catch?

**Method:** the 2026-08-27 synchronizer plan executed through its four increments on master.
Increment 1 (ingestion pre-work) and Increment 2 (the minimal `refresh-spec` synchronizer plus
the SSE Restore patch cut from PR #45182's source-only subset) landed with red/green tests and a
byte-identical `generate --verify` at the old pin. Prepare then ran live against the resolved
`v2` tip and its receipt was maintainer-reviewed before apply.

**The refresh:** the accepted snapshot moved `a6a712a3` → `954cdc7b` through the receipt — 133
operations (+22/−9 vs the pin), 336 components, `contentSchema` restored at 2 by the patch, both
touched-file preimages recorded, and the receipt's `rawSha ≠ baselineSha` surfacing doc 21 T7
(upstream's committed document stale against its own generator) mechanically on every prepare.
The nine removals were the Q137/Q139 drift set plus the fully deleted question family, including
the two shipped operations; the profile moved 82 → 79 selected / 54 pending after
`pty.connect.token` deselected — its new `x-opencode-ticket` header parameter refused at the
Increment 1 binder wall exactly as Q146 predicted, and ADR-0021's hand-written family owns it
next. The eight `persistentPty.*` operations entered as pending through reason-bearing identity
rows; the family also gained session-scoped terminal routes upstream since Q149's measurement.

**What the walls caught, and the decisions they forced (maintainer, 2026-08-27):**
(1) the pin-era `…1` duplicate class (twelve `Form.*1` name rows, seven aliases) went stale and
retired; the restored event tree instead carries `_N`-suffixed near-duplicates of the
operation-side message models, collapsed through structurally validated aliases so stream
payloads and one-shot models stay one C# type — while the structural-equivalence probe's
`ProviderState_N → Form.Metadata` coincidence was refused by hand: sixteen provider-state
records alias within their own family onto the unsuffixed component, never across the semantic
boundary (doc 21 O6's blind spot, exercised for real). (2) Effect's beta.103 `*Encoded`
fallback rename leaked encode-side names into ~60 public models; sealed as a mechanical dialect
rule — `ProjectionArtifactNamePolicy` strips the suffix from derived names unless the unsuffixed
component exists (the guard keeps `V2Event`/`V2EventEncoded` distinct), the same class of rule
as the `v2.` prefix strip and deliberately reusable for future artifacts; if upstream stops
emitting the artifact the rule goes quietly dead. (3) `Session.Message.Assistant.Tool_1`'s
promoted state member collided with its twin — resolved by the alias family, not a naming row.

**Surface drift absorbed:** `CommandEvaluationError` → `CommandExecutionError` (genuine upstream
rename), `ForbiddenError` and `QuestionNotFoundError` gone, `PluginInfo` restructured into the
`IPluginInfo`/`IPluginSource` unions, `session.interrupt` upgraded from a 204 to a typed 200
`SessionInterruptResponse`, and the restored event closure added `PersistentPty*`,
`CredentialSwitched`/`CredentialUpdated`, and `InstructionEntrySnapshot` models. The
removal-bearing PublicApi baseline was reviewed and accepted with all four TFMs byte-identical;
`ProjectIcon.Url` joined the established CA1056 plain-wire-string arbitration.

**Evidence:** the full local gate is green at 2,333 tests; `generate --verify` is current at
79/54; `refresh-spec --verify` reproduces the committed receipt (`spec/receipt.json`); and the
committed sandbox's standing walkthrough ran live against a server built from `954cdc7b` —
health, the session breadth set, the permission round trip, compact's inbox item, the deliberate
fork 400, and the full mechanism leg all answered as declared, with `interrupt`'s new typed 200
observed on the wire. Pinned-fixture tests now ingest through the production identity-map path
(`BindingTestHost.LoadPinnedInputsAsync`), and the smoke-test landmarks moved with the document
(`Config.InfoEncoded#/properties/formatter`, `WorktreeErrorEncoded`/`UnauthorizedErrorEncoded`).

## Q151: What did the typed-location + PTY family arc land, and what did the live proof show?

**Method:** the 2026-08-27 arc plan executed through its six tasks on master, each with red/green
tests and a green gate. Task 6 ran the committed sandbox's standing walkthrough — extended with a
new PTY leg — against a server built from the pinned submodule (`bun src/index.ts serve --hostname
127.0.0.1 --port 4137` from `external/opencode/packages/cli`, bun 1.4.0, Windows 11), and probed
the same two doors with raw `curl` requests so the wire facts do not depend on SDK code. The
submodule checkout never left the pin and gained no tracked change.

**What landed.** The arc opened with a document-identical refresh moving the accepted snapshot
`954cdc7b` → `803ead32` (the surface is unchanged; only the identity moved). Then, in order:
`OpenCodeRequestOptions.Location`, merged member by member over the ambient location inside
`RequestDecorationPolicy` so a per-call scope reaches every route without route branching; a
curation-declared **internal-raw emission mode** in the generator (internal clients and adapters,
public wire models and envelopes, no public methods) together with the internal header channel
that carries document-declared header parameters to the wire — reachable only from generated
internal-raw methods and hand-written doors, and deliberately general because
`server.experimental.persistentPty.connectToken` declares the same header; the hand-written
`PtysClient`/`PtyClient` over those internal raw clients (ADR-0021); a curated `transportOwned`
SHA-256 fingerprint over `v2.pty.connect`'s ingested subtree, the only generation-time check that
a refresh reshaping the never-selected WebSocket operation fails loudly; and `PtySession`, the
family's live working object. The profile stands at **81 selected / 52 pending** (`pty.list` and
`pty.connect.token` joined it), and the full local gate is green at **2,714 tests**.

**The ticket-less Basic upgrade, confirmed on the wire.** Upstream's authorization middleware
skips the credential check only for a URL carrying a connect ticket — the exemption exists because
browsers cannot set headers on a WebSocket upgrade — so a non-browser client should be able to
upgrade with the ordinary `Authorization` header and no ticket at all. Both design reviews settled
this from source; this is the measurement. The same upgrade request, twice:

```text
$ curl -i -m 3 http://127.0.0.1:4137/api/pty/<id>/connect \
    -H "Connection: Upgrade" -H "Upgrade: websocket" \
    -H "Sec-WebSocket-Version: 13" -H "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ=="
HTTP/1.1 401 Unauthorized
www-authenticate: Basic realm="Secure Area"

$ curl -i -m 3 -u opencode:<password> http://127.0.0.1:4137/api/pty/<id>/connect  … (same headers)
HTTP/1.1 101 Switching Protocols
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=
```

The SDK therefore never mints a ticket for its own connection, and `CreateConnectTokenAsync` stays
the door for handing a browser one. The token door's own header requirement was measured the same
way: `POST /api/pty/<id>/connect-token` answers **403** without `x-opencode-ticket` and **200**
with `x-opencode-ticket: 1` (a 36-character ticket, `expires_in: 60`) — the internal header
channel, proven end to end without a public header facility.

**The observed frame sequence.** One walkthrough run, verbatim (`chars` counts decoded UTF-16
units; `cursor` is the absolute output cursor the server reports):

```text
pty-create:  status=200 command=C:\Program Files\PowerShell\7\pwsh.EXE state=Running pid=75516
pty-token:   status=200 ticket=<redacted, 36 chars> expiresIn=60
pty-connect: upgrade answered 101 — ticket-less, Basic credential on the upgrade request
pty-replay:  outputFrames=1 chars=16   cursorFrames=1 cursor=16   end=Target
pty-write:   sent echo hello\r
pty-echo:    outputFrames=8 chars=1688 cursorFrames=0 cursor=<none> end=Target
pty-output:  outputFrames=3 chars=441  cursorFrames=0 cursor=<none> end=Target
pty-resume:  from=16   outputFrames=1 chars=2129 cursorFrames=1 cursor=2145 end=Target
pty-remove:  status=204 isError=False
pty-close:   from=2145 outputFrames=0 chars=0    cursorFrames=1 cursor=2145 end=ServerClose
```

Exactly one cursor frame ends the replay and none follows it: the eight and three live frames
after the write carry no control frame, so a reader that stores the one cursor has stored
everything the resume contract needs. The arithmetic closes: `16 + 1688 + 441 = 2145`, and
reconnecting at cursor 16 replayed `2145 − 16 = 2129` characters — the delta only, never a
second full replay — while reconnecting at 2145 replayed nothing and still received its cursor
frame.
Replay arrives as **one** frame where the same bytes arrived live as eleven, which is the server's
64Ki replay chunking seen from the other side. Removing the PTY while a read was in flight ended
the enumeration normally rather than faulting it, as the close-status policy intends.

**Unexpected: a line feed is not Enter.** The first live attempt wrote `"echo hello\n"`. The
terminal echoed the characters and then waited forever: a terminal's Enter key is **CR**, and
PowerShell's line editor treats LF as an insert, not an accept. `"echo hello\r"` executes. Two
consequences: the walkthrough sends CR, and any future PTY test writing a command must do the
same. A second, smaller finding rides along — PSReadLine's rendering is not stable across runs
(history prediction rewrites the line, and the screen is repainted with absolute cursor moves), so
matching a marker on the command's *result* is unreliable where matching the terminal's echo is
not. The leg therefore reads until the echo appears and then drains the stream for a bounded
window, reporting whatever the terminal produced instead of asserting a shape.

## Q152: What did the third receipt-governed refresh land, and what did its walls catch?

**Method (2026-08-28):** the per-session cadence (ADR-0020) opened with `refresh-spec --ref
origin/v2`. The fetch reported a **forced update** — `803ead32` → `d2ee536c` is not a
fast-forward (`git merge-base --is-ancestor` refuses), so upstream manages `v2` with history
rewrites and the old pin left the branch's reachable history. Commit-range diffs between pins
are therefore unreliable evidence; the receipt's content hashes are the trustworthy comparison,
and the pin move itself removes the fragility of pinning a rewritten-away commit. The receipt
was maintainer-reviewed before apply.

**The refresh:** the accepted snapshot moved `954cdc7b`-era `803ead32` → `d2ee536c` — 134
operations (+1/−0), 339 components (+3), `contentSchema` restored at 2 by the unchanged SSE
patch. The single protocol-visible upstream commit is `762291b2a8` ("durable session metadata
at creation", #45805). The delta: the new `server.experimental.persistentPty.handoff` (a plain
HTTP POST answering the nullable-payload envelope `{"handoff": PersistentPty.Handoff | null}` —
exactly the represented-nullable shape the queued envelope-completion lane binds), the
`Session.Metadata` free-form record riding session create/created/info as an optional
dictionary, a new 404 `SessionNotFoundError` arm on `session.import` (parents import before
children), and one more stabilize duplicate, `Session.Message.ToolState.Running_1`.

**What the walls caught:** (1) the T3 identity wall refused the new operation's leaked group id
(`server.experimental.…` without the `v2.` prefix); one more reason-bearing
`operationIdentities` row maps it to `v2.persistentPty.handoff` beside its eight siblings.
(2) the alias structural-identity check refused `Session.Message.Assistant.Tool_1` because its
state union now references `ToolState.Running_1`, which had no alias row; the pair proved
byte-identical under sorted-JSON diff and collapsed through one more `schemaAliases` row with
the established event-tree reason. Both refusals were loud, named the exact schema, and needed
no mechanism changes.

**SSE assumption re-verified at `d2ee536c`:** the Restore patch applied rather than refusing, so
the repair predicate still holds — raw upstream's `V2EventEncoded`/`SessionLogItemEncoded` still
lack `contentSchema`; both touched-file preimages are byte-identical to the patch's pins, and
`session.ts` still declares the `SessionLogItem` stream union through `HttpApiSchema.StreamSse`.
PR #45182 remains open (`needs:issue`); `rawSha ≠ baselineSha` persists (doc 21 T7).

**Evidence:** the full gate is green — 2,767 tests, `generate --verify` current at 81/53,
`refresh-spec --verify` reproducing the committed receipt. The PublicApi delta is three
additive optional `Metadata` properties with all four TFM snapshots byte-identical; the
reviewed baseline was accepted.

## Q153: What did the M4 launcher arc land, and what did its live checkpoints prove?

**Method (2026-08-28):** the maintainer-approved arc plan (every decision resolved pre-execution)
ran task-by-task on an isolated worktree branch with a fresh implementer per task, an independent
task review per task, scoped re-reviews per fix round, and a final whole-branch review (verdict:
ready to merge; Standards 0C/1I, Spec 0C/0I). Six tasks, four fix rounds total, all converged in
one or two rounds. The branch merged into master at `4ec11b0` with the full gate green at
3,182/3,182 — the ReservedNamePolicy mirror gained the three launcher spine names atomically with
the types, and the PublicApi baseline union was proven by its own test rather than trusted.

**What landed:** `OpenCodeServer.StartAsync` — the standalone door (ADR-0001): hand-rolled on
`System.Diagnostics.Process`, `serve --stdio --port 0`, caller-generated `OPENCODE_PASSWORD`,
event-based dual-stream drain, single-JSON-line readiness, stdin ownership lease, and a bounded
teardown ladder (grace → tree kill → forced wait) with every failure path drain-bounded after the
review closed the plan's own unbounded-`WaitForExit` hang class. `CreateClient` pins identity
fail-closed (caller-set Endpoint/Username/Password refuse; fresh options instance, never mutating
the delegate's) with a reflection mirror test that trips on any future options member. Test
infrastructure: `PinnedServerCommand`/`ServerIsolation`/`TestRunRoot`, real-process lifecycle
acceptance (8 scenarios × 4 TFMs green locally, incl. the net472 taskkill arm),
`PinnedOpenCodeServerFixture` over a test-only CliWrap adapter (failure-path log retention made
reachable after review caught the plan's dead-code path; double-dispose guarded), the
`DriveController` JSON-RPC client (id-correlated round trips, notification buffering, bounded
everything), and the ADR-0022 workflow test — day-one blocking, no skip mechanism.

**Live checkpoints:** (1) the plan's highest-risk unverified item — a config-seeded
`providers.sim` over the builtin openai-compatible provider — resolved end to end: the live
`llm.request` arrived at the claimed chat URL with the seeded model id (now asserted in the gate,
not just observed); upstream's model resolver has no fallback for an explicit `ModelRef`, so the
determinism is structural. (2) The sandbox `--standalone` demo: the SDK started the pinned server
itself, health answered with the child's own pid. (3) A real 1-in-3 flake (drive port-reservation
race) was fixed as a bug per the gating decision — `DrivePortGate` holds a machine-wide file lock
from reservation until a completed WebSocket connect proves the bind.

**Upstream findings (report candidates):** `session.idle` is deprecated at the pin with no
publisher anywhere while `SessionIdle` stays in the public event union — the workflow's terminal
event is `SessionExecutionSucceeded` (`execution.ts`, one terminal observation per busy period).
Bun's workspace/JSX discovery keys on the working directory, not the entry path — the pinned
server must run with cwd at `packages/cli` (now recorded in the fixture, the README recipe, and
this log). The simulation catalog is not empty (models.dev bundle loads); determinism rides the
explicit `ModelRef`.

**Outstanding:** the three-OS hosted matrix proof (maintainer-gated push; first hosted run of the
bun legs and `FileShare.None` on Unix is the named risk), the queued service-parity arc
(Discover/Ensure/Stop), and the named post-integration work: fixture retention/disposal
consolidation across the two tests/Shared fixtures, `OpenCodeServer` post-dispose guards, three
cheap contract tests (composer backslash-before-quote, negative grace timeout, non-object
readiness root), and `Dispatch`'s malformed-notification hardening at the next controller touch.
