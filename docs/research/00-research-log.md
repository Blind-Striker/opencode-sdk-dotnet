# Research log — 2026-08-08

> How this project's understanding was built: the questions asked, how each was
> researched, what was found, and what decision or lesson came out of it.
> Chronological. Details live in the numbered topic docs; this is the chain.

Reference repos used throughout (git submodules):

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
→ GOAL.md TODO. Parked: full editorconfig/analyzer contradiction review.

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

Trigger: the parked analyzer items in GOAL.md plus a ChatGPT conversation the owner
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

## Standing conclusions

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
