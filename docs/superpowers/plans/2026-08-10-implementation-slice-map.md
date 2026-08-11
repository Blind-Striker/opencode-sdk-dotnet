# Implementation Slice Map — SDK build-out

Date: 2026-08-11

> **For agentic workers:** this is the **master map**, not an executable plan. Execution
> happens through per-slice plan files in this directory (checkbox tasks, written
> just-in-time — see "Planning model" below) via deniz-process:subagent-driven-development.
> Do not implement from this document alone.

**Goal:** build `OpenCode.Sdk`, `OpenCode.Sdk.Extensions`, and the `tools/` generator from
the three sealed design specs, as vertical slices that each end in working, tested,
analyzer-clean software.

**Inputs (canonical, sealed):** `specs/2026-08-09-public-api-design.md`,
`specs/2026-08-09-generator-architecture.md`,
`specs/2026-08-10-testing-architecture-design.md`; ADRs 0001–0009; `AGENTS.md` locked
decisions. This map adds sequencing and scope-cutting only — design content lives in the
specs; where this map summarizes them it relays, never overrides.

## Planning model (sealed 2026-08-10)

- **Just-in-time detail plans.** Each slice gets its own plan file
  (`YYYY-MM-DD-slice-NN-<name>.md`, writing-plans format) written at or just before its
  execution session, so learning from landed slices feeds the next plan instead of rotting
  in a pre-written one. This map is the only document that spans all slices.
- **Progress model.** Task/step level: the slice plan's checkboxes. Slice level: one GitHub
  issue per slice, linking to this map (and to its plan file once written); ordering via
  native `blocked_by` edges; a slice whose detail plan exists gets `ready-for-agent`; the
  executing session claims with `--add-assignee @me`; the issue closes when the slice's PR
  merges. ROADMAP relays the issue range + this directory + a one-line status.
- **Execution model.** deniz-process:subagent-driven-development per slice. Commit rhythm
  (the `AGENTS.md` development-loop exception, sealed): on a slice branch, per-task commits
  need no per-commit approval; merging to `master` always does (PR review).
- **Branching.** No implementation on `master`. One worktree + branch per slice
  (`feature/slice-NN-<name>`, created at execution time via
  deniz-process:using-git-worktrees), one PR per slice targeting `master`.
- **Change management.** Specs and plans are falsifiable — deviations follow
  `docs/agents/deviation-protocol.md` (levels 0–3). Slice issues and plans link it.

## Done definition — every slice

1. Analyzer wall clean (TWAE, on-merit for generated output — no new exemptions without
   per-rule arbitration).
2. The slice's tests green on every leg the slice claims (TFM × OS per testing spec §4).
3. `dotnet format --verify-no-changes` clean.
4. `generate --verify` clean whenever the slice touches the tool or its outputs.
5. Docs pass in the same PR: ROADMAP one-liner, research-log entry when evidence is worth
   keeping, corrections to any doc the slice contradicted (deviation protocol).
6. Conventional Commits; PR merged with maintainer approval; slice issue closed.

## Slice sequence

Dependencies are `blocked_by` edges on the issues. The order below is the default
execution order; slice 6 may start any time after 4 (it only needs the client root), and
slice 11's `refresh-spec` half may be pulled earlier if an urgent spec refresh materializes.

### Slice 0 — Tooling skeleton

**Depends on:** — · **Spec anchors:** generator spec §3.1–§3.3

`tools/` layout: file-based entry (`opencode-tool.cs`, shebang + `#:project`, committed
executable bit), `OpenCode.Sdk.Tools` library (net10.0) with the `ToolApp` factory
(Spectre.Console.Cli + DI registrar) and a fail-loud `generate` stub;
`OpenCode.Sdk.Tools.Tests` (net10.0, `CommandAppTester`); the §3.3 verification list —
strict-props build, `#:project` cache staleness, invocation-form pinning — with its
recorded two-condition fallback (console-app promotion + one-line ADR-0003 correction);
CI smoke step running the entry on the Linux leg.

**Hands over:** `ToolApp.CreateRegistrar`/`ToolApp.Configure` composition seam; pinned
invocation forms; the Tools.Tests harness pattern.

### Slice 1 — Ingestion + SpecIR

**Depends on:** 0 · **Spec anchors:** generator spec §4.1, §11 (projection tests)

Foundation first: the full ToolApp composition root (`docs/engineering/coding-style.md`
§2: `IFileSystem`, `IAnsiConsole`, MEL Spectre/optional-file providers, global settings,
interceptor, and the shared production/test registration path), the Testably filesystem seam
and independent TestableIO analyzer joining the repo-wide wall, and the lambda-first
scenario/builder/fixture test infrastructure with promotion-only named scenarios
(`docs/engineering/testing-style.md`
§1). On that foundation, the
pinned Microsoft.OpenApi reader as the tooling ingestion layer; the fail-closed
projection behind the whitelist dialect wall (admitted typed members, the
unrecognized-keyword net, extension dispositions, library-upgrade tripwires); the
minimal immutable SpecIR (record inventory derived backward from
Binder/emitter/refresh-diff consumption); projection normalizations (duplicate-ref
dedup, envelope-shape classification, error-style detection, literal markers in both
dialects, special-value numbers, parameter-stripped media types, opaque
`x-effect-stream`, unrestricted `{}` nodes, the `prefixItems` fragment adapter).
Filesystem I/O via Testably's shared `IFileSystem` contract. Tests: projection quirk
fixtures loaded through the real reader + wall red tests + tripwires + the full-spec
landmark smoke test (no
count assertions). Honest note: the generation pipeline remains a fail-loud stub until
slice 3, while the complete hosting composition and its global CLI options become live
in this slice.

**Hands over:** SpecIR types consumed by the Binder; the full ToolApp hosting composition
and centralized scenario/builder test infrastructure every later slice builds on.

### Slice 2 — Binder + curation v0

**Depends on:** 1 · **Spec anchors:** generator spec §4.2, §5; ADR-0008

`tools/curation.json` v0 authored in full (groups/handles, envelope payload names,
exclusions, content-type map, parameter/property overrides with mandatory reasons, brand
spellings) — **curation rows are public-API review surface (ADR-0008); this slice's PR is
an API review.** Binder: curation load (`Disallow` + comment-skip + trailing commas,
`[JsonPropertyName]` pins), bidirectional coverage checks with batched categorized
reporting, reachable-closure computation with orphan info-listing, mechanical name
computation (FDG acronyms, dotted-name mangling, handle rule), derived emission decisions
(paginators, converters, registry, stream item schemas), XML-doc computation with the
deterministic synthesized fallback, fingerprint computation (both kinds). Tests: one red
test per coverage check, name-computation cases, handle routing, derivation cases.

**Hands over:** `EmitPlan` types consumed by all emitters; the fingerprint values the
Writer will persist.

### Slice 3 — Model-layer emission — `generate` becomes real; the 5-TFM milestone

**Depends on:** 2 · **Spec anchors:** generator spec §6–§9, §12, §13; ADR-0003/0004/0009

`ModelEmitter`, `UnionEmitter` (tolerant converters, sealed dispatch-as-data shape),
`RegistryEmitter`; the Writer (output manifest, stale cleanup, determinism, whole-project
`dotnet format` post-step); `generate` gains its real pipeline plus `--verify` (with the
dirty-paths precondition) and `--update-fingerprints`; `spec/fingerprints.json` written
and verified on every run; the committed full-spec model layer lands in
`src/OpenCode.Sdk` (plain `.cs`, non-magic header, manifest-tracked); CI's entry smoke
step upgrades to `generate --verify`. **Milestone (downlevel-early):** full-spec
generated output compiles on all five TFMs — the SDK gains its downlevel System.Text.Json
dependency and the ROADMAP net472 spike items *polyfill-set validation* and
*generated-model downlevel compile* resolve here, before any emitter polish. Also here:
round-trip behavior tests in `OpenCode.Sdk.Tests` (level 1: tag dispatch, unknown-variant
carrier + re-serialization, out-of-order discriminators, explicit-null vs missing),
per-emitter Verify micro-snapshots (Verify.TUnit enters CPM), the double-emit determinism
test, and the **CS1591 → `error` flip** (synthesized fallback docs make it holdable).

**Hands over:** generated models + serializer registry; a live regen-verify CI gate.

### Slice 4 — Transport core + Extensions (co-development)

**Depends on:** 3 · **Spec anchors:** public API spec §4, §5.1 (envelope base), §6, §7,
§9, §10; ADR-0007

Hand-written identity core, TDD against in-memory `HttpMessageHandler` stubs: exception
spine (`OpenCodeException`/`OpenCodeApiException`/`OpenCodeTransportException`), envelope
base (`OpenCodeResponse`, `[SetsRequiredMembers]` error path), `OpenCodeClientOptions` /
`OpenCodeRequestOptions` (`NoThrow`, `Directory`), the `ExecuteAsync` behavior core
(decoration: basic auth chain, directory header, User-Agent; idempotent-only retry with
one disable knob; tagged→typed error mapping incl. unknown-tag carrier;
throw-vs-populate; `ActivitySource` + `ILogger` telemetry; sync per-attempt hooks),
`SendAsync` escape hatch, `HttpClient` ownership rules, `OpenCodeClient` root with the
mock seam (virtual members, protected ctor, instructive `Pipeline` guard). Extensions:
`AddOpenCodeClient` (options binding, `AddHttpClient`, returns `IHttpClientBuilder`) +
`OpenCode.Sdk.Extensions.Tests` project. The **public API surface lock** (Verify surface
dump per package) starts here for both packages.

**Hands over:** the `Pipeline.ExecuteAsync<T>` seam, envelope base, and options types the
generated operation methods delegate into; the DI composition shape.

### Slice 5 — Operation surface (remaining emitters + contract tests)

**Depends on:** 4 · **Spec anchors:** public API spec §5, §8; generator spec §6; ADR-0008;
testing spec §3 (level 2), §7.1

`EnvelopeEmitter` (guarded getters, guarded `PrintMembers`, disposable `Stream`
envelopes, 204 envelopes, `SessionsCursor`), `InputRecordEmitter`,
`OperationMethodEmitter` (sub-clients, `SessionClient` handle, `Legacy` hub, one-line
virtual delegations, guards, XML docs with `<exception cref>`), `RoutesEmitter`,
`PaginatorEmitter`. Tool-emitted test artifacts land as the second manifest root: the
**operation inventory** (with HTTP method + path template) and the **contract fixtures**.
Level-2 contract suite in `OpenCode.Sdk.Tests/Contract/`: every operation's happy path +
every declared error response through the stub handler — 100% breadth on both surfaces,
running on all unit-leg TFMs (net472 leg = ns2.0 proxy). Surface lock extends to the full
generated client.

**Hands over:** the complete typed client; the inventory the auth sweep loops over;
`OpenCodeRoutes` for raw probes.

### Slice 6 — Launcher + three-OS acceptance

**Depends on:** 4 (5 recommended first — acceptance uses the typed health call) · **Spec
anchors:** public API spec §13; ADR-0001; testing spec §9.2

`OpenCodeServer.StartAsync` deep-dive: six-point anatomy (arg quoting per TFM, continuous
stdout/stderr drain, Unix SIGTERM grace, tree-kill fallback, Windows Job Object orphan
protection; net11 light-up deferred post-GA), the auto-port chain (`--port=0` first with
release-binary confirmation — a §13 UNVERIFIED item resolved here — then `TcpListener(0)`
probe + bounded retry), readiness-line parsing, `StartupTimeout` kill + captured output,
`CreateClient()` auth wiring. The ROADMAP net472 spike items *async stdout reading* and
*`taskkill /T /F` tree-kill* resolve here. `OpenCode.Sdk.Integration.Tests` project is
born (net472 Windows-only + net8/9/10): fake-binary tests (scripted stdout/exit) +
real-binary three-OS acceptance (auto-port ×2 concurrent, explicit-port conflict
fail-loud, dispose = whole tree gone, env injection) + helper-process orphan technique.
CI: pinned opencode install on all three legs; `spec/opencode-version` created
(hand-stamped now; `refresh-spec` owns it from slice 11).

**Hands over:** the launcher the direct-mode fixture dogfoods; the integration project
and its CI legs.

### Slice 7 — Integration harness + fake LLM + first scenario wave

**Depends on:** 5 + 6 · **Spec anchors:** testing spec §5, §6, §7.2 (stage 1), §8

Fixture family: common base + `DirectOpenCodeServerFixture` (launcher-started, §5.2
env-isolation set verbatim) + `DedicatedOpenCodeServerFixture`; the workspace model
(`CreateWorkspace()`, `ServerPath`/`HostPath` views); `[Quarantined]` attribute + the
quarantine conventions; the fake LLM server (Kestrel, port 0,
`POST /v1/chat/completions` only: scripted reply queue incl. failure modes, auto-`"ok"`,
title auto-answer, per-reply `Usage`, deterministic chunking, `WaitForRequestsAsync` +
`Hits`/`Inputs`; `[NotInParallel("llm")]` group); `[ExercisesOperation]` attribute + the
coverage gate at **stage 1 — reporting mode**: the declaration-vs-inventory diff runs and
prints the uncovered list on every run but does not fail the build yet (sealed
interpretation: the gate is born here, its hard-fail flip is slice 10's exit criterion).
First scenario wave: session/message CRUD, config, fs, catalog reads, one prompt
round-trip through the fake LLM.

**Hands over:** fixtures, workspace, fake LLM, gate plumbing for slices 8–10.

### Slice 8 — SSE engine + stream scenarios (streams-early seal)

**Depends on:** 7 · **Spec anchors:** public API spec §4.5, §11; testing spec §9.1;
ADR-0009

The hand-written SSE engine, TDD at level 1 with canned streams (`SseParser` over
`ResponseHeadersRead`, lazy `IAsyncEnumerable`, cancellation, exception surfacing from
`MoveNextAsync`, unknown-event tolerance — level 1 is its honest home); hand-wired stream
endpoints (`client.Events.SubscribeAsync`, `session.Events.SubscribeAsync(after)`,
`ListHistoryAsync`) delegating item typing to the generated unions. The six §9.1
integration scenarios in the same slice — live sequence, **durable gap-free resume**,
mid-stream disconnect (dedicated fixture), `session.error` union via fake-LLM failure
modes, numeric `after`/`limit` (the designated behavior-premise catch point), concurrent
streams + CRUD — including the net472 `ServicePointManager.DefaultConnectionLimit` fix
and its standing regression test (the last ROADMAP net472 spike item resolves here).

**Hands over:** the complete stream plane, exercised end-to-end in direct mode.

### Slice 9 — Container mode (clean-install lane)

**Depends on:** 7 (+8 for the inherited stream suites) · **Spec anchors:** testing spec
§5.3, §11.2, §14

Clean-install Dockerfile (pinned opencode) + GHCR image build/push workflow (pin-tagged,
rebuilt only on Dockerfile/pin change); `ContainerOpenCodeServerFixture` (Testcontainers:
workspace bind mount at `/workspace`, host-port exposure for the fake LLM, port + health
readiness, Docker-unavailable conditional skip); the §14 UNVERIFIED items (Testcontainers
API shapes, GHCR anonymous pull, health-probe choice) resolve here; selective dual-mode
suites via abstract base + `[InheritsTests]` for process, workspace/filesystem, and
stream-sensitive scenarios plus basic container health/typed-CRUD smoke; Linux container
CI legs for net8.0, net9.0, and net10.0 (net472 remains Windows/direct only).

**Hands over:** the process-management control lane ("direct red, container green
indicts the launcher").

### Slice 10 — Coverage completion — the gate turns green

**Depends on:** 8 · **Spec anchors:** testing spec §7.2 (stage 2), §7.3; ADR-0005

The remaining modern-surface coverage to 61/61 (one workflow may declare and observe
multiple operations; deep scenarios concentrate on risk, with honest `ErrorPathOnly`
declarations for the console-backed `integration.connect.*`/`integration.attempt.*`
family); the
auth + reachability sweep (dedicated password-enabled instance, sequential, data-driven
over the inventory, `SendAsync` + `OpenCodeRoutes` probes, curated probe table with
mandatory reasons + `authOnly` flags, SSE probes via `ResponseHeadersRead`); the status
ledger (tallying `DelegatingHandler` + closing gate: every modern op observed ≥1 2xx);
the skip-ban reflection gate (whitelist: conditional Docker skip, `[Quarantined]`);
**the coverage gate flips from reporting to hard-fail** — this slice's exit criterion.
Legacy scenarios stay best-effort until the MCP server's consumed set arrives (ADR-0005).

**Hands over:** the fail-closed coverage regime the MCP phase will extend.

### Slice 11 — Operational closure

**Depends on:** 3 (`refresh-spec`); 10 (canary/quarantine steps) · **Spec anchors:**
generator spec §10; testing spec §8.2, §11.3–§11.4

`refresh-spec --ref` command + tests (faked git/copy wrappers: submodule bump, spec copy,
`SNAPSHOT.md` rewrite, `spec/opencode-version` stamp, SpecIR diff summary);
`spec/SNAPSHOT.md`'s refresh section rewritten to the tool-based playbook; the nightly
non-blocking canary against `opencode@latest` with label-deduped issue filing; the
non-blocking quarantine CI step (category filter). **Phase close rides this slice:**
durable sealed decisions without an ADR home distill into ADRs, the transient
`docs/superpowers/` specs and this map retire, ROADMAP shrinks to the MCP phase.

## Sequencing summary

```
0 → 1 → 2 → 3 → 4 → 5 → 7 → 8 → 9
                 └→ 6 ──↗       └→ (9 inherits stream suites)
                            8 → 10 → 11 (canary/quarantine)
                 3 ─────────────────→ 11 (refresh-spec half)
```

Slices are sized for one focused execution arc each; if one proves oversized mid-flight,
splitting it is a level-0/1 call recorded on its issue — merging two is the same in
reverse.

## Decisions sealed in the planning session (2026-08-10)

1. **Launcher before streams.** The handover skeleton's SSE→launcher order inverted: the
   direct-mode fixture dogfoods the launcher (testing spec §5.2), and streams-early binds
   the SSE engine to its integration scenarios — so launcher (6) and harness (7) precede
   streams (8).
2. **Coverage-gate staging.** "Hard gate from day one" is implemented as: gate born in
   slice 7 (reporting mode, diff visible on every run), hard-fail flip as slice 10's exit
   criterion. Never optional once the suite claims completeness.
3. **Test projects are born in the slice that needs them** (Tools.Tests → 0,
   Extensions.Tests → 4, Integration.Tests → 6), per testing spec §4's own staging.
4. **net472 spike items distributed to their SUT slices:** polyfill/model downlevel → 3,
   stdout/tree-kill → 6, `ServicePointManager` SSE → 8.
5. **CS1591 → `error` flips in slice 3** (synthesized generated docs make it holdable;
   hand-written surfaces are documented as they land).
6. **JIT planning + this map** as the plan architecture; slice issues with `blocked_by`
   edges; `ready-for-agent` only with a written detail plan.
7. **Execution:** subagent-driven-development; per-task commits free on slice branches
   (the AGENTS.md development-loop exception), master merges by PR approval only.
8. **Branching:** worktree + `feature/slice-NN-<name>` per slice, one PR per slice.
9. **Deviation protocol** (`docs/agents/deviation-protocol.md`) governs all mid-slice
   contradictions; AGENTS.md carries the falsifiability working agreement.
10. **Slice 1 hosting and test-authorship correction:** the ToolApp foundation is the
    full PathSmith-shaped DI host rendered through MEL; small one-off spec variations are
    inline scenarios, while named scenario classes require reuse, complexity, or durable
    domain identity. Every test case and full per-task gate remains.
11. **Integration breadth without ceremonial depth:** modern 61/61 declaration + observed
    2xx remains hard; workflows may cover multiple operations and deep assertions are
    risk-based. Dual-mode is selective, and its Linux container set runs on every modern
    runtime TFM (`net8.0;net9.0;net10.0`).
