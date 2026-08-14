# Research log — 2026-08-08

Date: 2026-08-13

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
non-empty password). The username is configurable (`--username`/`OPENCODE_SERVER_USERNAME`,
default `opencode`) — our pipeline's hardcoded `opencode` user is incomplete. Client-side
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
