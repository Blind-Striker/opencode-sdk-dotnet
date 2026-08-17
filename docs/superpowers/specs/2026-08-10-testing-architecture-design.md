# Testing Architecture & Strategy — OpenCode.Sdk, OpenCode.Sdk.Extensions, tools/

Date: 2026-08-13

> **Status: vision / reference — not sealed.** Binding decisions live in the ADRs and
> `AGENTS.md`; this document is direction and design rationale, not law. Contradicting it
> is a finding to note, not a deviation-protocol event.
>
> The v1.x dual-surface facts here (operation counts, the legacy hub, envelope inventories)
> predate the 2026-08-13 v2 retarget (ADR-0005) and are historical; the mechanisms remain
> reference material.

Design specification produced by the testing-architecture brainstorm session (2026-08-09/10,
ROADMAP queue item 1 — its final design step). Every decision below was discussed and sealed
individually with the maintainer. This spec owns the ROADMAP Open Questions item "Testing
strategy details". The holistic grill session (2026-08-10, research log session 9)
stress-tested this spec against the other two specs, re-verified its upstream claims at
source level, and hardened it in place; next, `writing-plans` produces the multi-phase
implementation plan (vertical slices co-developing `tools/`, SDK, Extensions, and tests).

## 1. Scope and inputs

**In scope:** the holistic test architecture and strategy across `OpenCode.Sdk`,
`OpenCode.Sdk.Extensions`, and `tools/` (OpenCode.Sdk.Tools) — test levels, project layout, the
real-process harness, the fake LLM fixture, endpoint-coverage mechanization, determinism and
quarantine policy, stream/launcher scenario sets, tooling tests, and CI wiring. The future MCP
server joins the same architecture (level 3 scenarios of its own; it also activates the
consumer-driven legacy coverage gate, §7.2).

**Out of scope:** implementation-phase mechanics flagged to build-out (§14); the launcher's
implementation deep-dive (public API spec §13); generator internals (generator spec).

**Inputs:** the public API spec's behavior contract (error spine §4, envelope guards §5,
`NoThrow` §6, mock seam §7.4, transport/retry §9, streams §11, launcher §13); the generator
spec's §11 testing sketch (revision authority exercised here — §10); ADR-0001 (launcher
three-OS acceptance), ADR-0002 (net472 as ns2.0 proxy), ADR-0005 (consumer-driven legacy
scope), ADR-0009 (unknown-variant tolerance).

**Evidence base — verified this session.** Upstream's test infrastructure was read at line
level in the submodule; reference-repo claims were read from the actual workflow files on
GitHub (`localstack-dotnet/localstack-dotnet-client`,
`localstack-dotnet/dotnet-aspire-for-localstack`). The load-bearing upstream facts:

- **`test:httpapi` is a route-coverage harness**
  (`packages/opencode/test/server/httpapi-exercise/`): the operation inventory is derived at
  runtime from the server's own API definition (`OpenApi.fromApi(PublicApi)`) and diffed
  against a scenario list; `--fail-on-missing` fails any route without a scenario,
  `--fail-on-skip` fails placeholder scenarios. Three modes: `coverage` (no requests),
  `auth` (per-route 401-without-credentials / non-401-with probes), `effect` (full in-process
  scenario execution). It runs in-process — no spawned server, no sockets.
- **Upstream tests never hit a real LLM.** Three tiers: `TestLLMServer`
  (`packages/opencode/test/lib/llm-server.ts`) — an in-process fake OpenAI-compatible SSE
  server on port 0 with a scripted reply queue including failure modes (`hang`,
  `streamError`, `httpError`, connection `reset`), auto-`"ok"` for unqueued requests, and a
  fixed auto-answer for title generation; an ordinary provider config row (`test/test-model`,
  `@ai-sdk/openai-compatible`) pointing at the fake, injected via `OPENCODE_CONFIG_CONTENT`;
  and a first-party VCR package (`packages/http-recorder`) replaying committed cassettes. The
  bun test preload deletes every real provider API key from the environment; CI passes no LLM
  secrets.
- **Upstream's tests do not use the SDK's `createOpencodeServer`.** Their subprocess fixture
  (`packages/opencode/test/lib/cli-process.ts`) spawns the real CLI with `--port 0` + stdout
  parsing ("Hard-coded ports flake under parallel tests" — their comment) and a full
  env-isolation set (§5.2). The SDK helper's fixed default port 4096 never faced test
  pressure — a cautionary precedent for not dogfooding.
- **Upstream has no container-based test harness** — its Dockerfiles are distribution and CI
  toolchain images only. The containerized clean-install lane here is our own design; its
  precedent is the reference repos' Linux-gated container legs.
- **Reference-repo CI patterns** (read from the workflow files): both repos run three-OS
  matrices with `dorny/test-reporter` TRX flows; container-backed tests are Linux-only
  (repo 1 via a `--skipFunctionalTest` build-script flag, repo 2 via step-level
  `if: runner.os == 'Linux'`); repo 1 runs a daily non-blocking canary against floating
  latest dependencies (its header comment: pinned CI missed a breaking change for 70 days).

## 2. Principles

1. **A three-link assurance chain.** Level 1 proves *our code is right*, level 2 proves *we
   read the spec right*, level 3 proves *we actually agree with the real server*. Each level
   catches a failure class the others cannot; none substitutes for another.
2. **Determinism first.** No real LLM and no API keys anywhere in CI; no fixed ports
   (port 0 everywhere); no sleep-based synchronization (§8).
3. **Fail-closed coverage.** The modern surface's scenario coverage is a build-breaking gate,
   not a report (§7) — upstream's `--fail-on-missing` rendered in TUnit.
4. **Dogfood by design, with a built-in control.** The direct-mode harness starts servers
   through our own launcher; container mode manages the process itself and never touches the
   launcher. The modes are engineered so that process management is the only *free* variable:
   their other structural differences — filesystem namespace and fake-LLM reachability — are
   pinned by the fixture (workspace bind mount and host-port exposure, §5.3), so
   "direct red, container green" still indicts the launcher (§5).
5. **Same-source circularity is acknowledged, not hidden.** Level 2's fixtures and the
   generator derive from the same spec: a misread spec produces models and stubs that agree
   with each other. Only level 3 (and its mechanical sweep, §7.3) breaks the loop. The sweep
   breaks it at reachability; the status ledger (§7.2) proves the intentional scenario layer
   spans the modern surface's success paths — and those paths inherently exercise typed
   deserialization against real responses, counted as **defense-in-depth, never as a
   substitute for the intentional levels 1–2**: incidental coverage never cancels an
   intentional test. A generic real-response schema validator is rejected on mechanism: its
   subject is upstream's spec↔server conformance (upstream's own harness tests exactly
   that), and it would turn the deliberate runtime tolerance (§2.1 / ADR-0009) into CI noise
   on upstream's hourly-beta cadence.
6. **Fake only published contracts.** A counterparty is faked only where upstream itself
   publishes and stabilizes the contract for test use — the LLM qualifies: the
   openai-compatible wire protocol behind an official provider `baseURL` config affordance.
   Upstream-private protocols — the console/SaaS backends behind the integration family —
   are never reverse-engineered into fakes: that is the `opencode-mcp` trap class (research
   doc 03) moved onto the wire, with no pin and no fingerprint radar to see it drift, and
   its CI reds would say "upstream's console moved", never "our SDK broke". Operations whose
   success path needs such a backend carry an honest `ErrorPathOnly` declaration instead
   (§7.2). Reversal trigger, recorded: the MCP server's consumed set (ADR-0005) grows to
   include integration operations *and* upstream ships a documented console-URL override
   affordance — then a minimal fake is re-evaluated as extend-only.

## 3. Test levels (the backbone)

| Level | Proves | Runs against | Lives in |
|---|---|---|---|
| **1 — Unit** | Internal logic of our code: converters, envelope guards, retry, SSE engine parsing, launcher pieces; tools projection/binder/emitters; Extensions DI composition | Nothing external — in-memory `HttpMessageHandler` stubs, canned payloads and streams | `OpenCode.Sdk.Tests`, `OpenCode.Sdk.Extensions.Tests`, `OpenCode.Sdk.Tools.Tests` |
| **2 — Contract** | Every operation's happy path + every declared error response maps to the right model/envelope/exception — mechanical 100% endpoint breadth | Tool-generated fixtures (from SpecIR) fed through the same in-memory stub handler | `OpenCode.Sdk.Tests` (`Contract/` area) |
| **3 — Integration** | The SDK works for real: real HTTP, real serialization boundary, real SSE over the network, real process lifecycle | A real `opencode serve` via the dual-mode harness (§5), fake LLM where assistant activity is needed (§6) | `OpenCode.Sdk.Integration.Tests` |

Level 2's "every operation" means both shipped surfaces: modern and legacy success/error
bindings are generated and tested under the same regen-verified mechanism. ADR-0005's
consumer-driven legacy limit governs deep level-3 scenarios, not contract coverage of public
methods the package ships.

**Mocking-framework decision (sealed): no WireMock, no third-party mock server at any
level.** WireMock (Java and .NET alike) has no SSE/streaming support — the body is delivered
whole, delays and faults are whole-response only (wiremock/wiremock#460, open for years) —
which kills exactly the mid-stream failure scenarios this design values. Below the socket, the
in-memory handler stub is strictly stronger (byte-level stream control, all TFMs including
net472, no socket flakiness); above it, behaviors that genuinely need a socket deserve the real
server, not a mock. Surveyed alternatives are recorded for the record: MockServer supports SSE
but is a JVM dependency; MSW is JS service-worker interception (wrong ecosystem); LLM-specific
mock servers (llmock, mock-llm, AI-Mocks) are purpose-built but each drags a foreign runtime
(Node/JVM) into three-OS CI with unverified failure-mode scripting. Hence the hand-rolled fake
LLM fixture (§6) — the same conclusion upstream reached for itself.

## 4. Projects and the TFM × OS matrix

```
tests/
├── OpenCode.Sdk.Tests/               ← levels 1+2 (Contract/ is a folder, not a project)
├── OpenCode.Sdk.Extensions.Tests/    ← level 1 (added when Extensions gains real code — ROADMAP queue 2)
├── OpenCode.Sdk.Tools.Tests/         ← level 1 (name already fixed by generator spec §3.1)
└── OpenCode.Sdk.Integration.Tests/   ← level 3: dual-mode harness, fake LLM, launcher acceptance
```

| Project | TFMs | OS |
|---|---|---|
| OpenCode.Sdk.Tests | net472 (Windows-only) + net8.0/net9.0/net10.0 | 3 OS |
| OpenCode.Sdk.Extensions.Tests | same | 3 OS |
| OpenCode.Sdk.Tools.Tests | net10.0 only (the tool runs single-TFM; a matrix proves nothing) | 3 OS (path/git behavior is OS-sensitive) |
| OpenCode.Sdk.Integration.Tests | net472 (Windows-only) + net8.0/net9.0/net10.0 — **all TFMs, maintainer decision** | 3 OS direct; Linux container on net8.0/net9.0/net10.0 |

Rationale notes: the contract area shares the unit project because its dependency set is
identical (no process, stub handler, fixture JSON) — separation is by folder/namespace and
test category, matching the reference repos' process/no-process split. Integration is a
separate project because its run profile differs (duration, opencode-installed precondition,
distinct CI gating). netstandard2.0 has no runtime; the net472 legs are its proxy coverage
(ADR-0002) — the integration net472 leg is the *behavioral* half of that claim
(`HttpWebRequest`-based handler stack, `ServicePointManager` limits, long-lived SSE on
Framework). The Linux container lane runs every runtime-capable modern TFM
(`net8.0;net9.0;net10.0`); net472 remains Windows/direct only (§11.2). Launcher acceptance
tests live inside Integration.Tests in their own folder and do
**not** use the dual-mode harness — the launcher is the SUT there (§9.2). The fake LLM fixture
lives under Integration.Tests (`Support/`) until a second consumer forces extraction (YAGNI).

## 5. The real-process harness (dual-mode fixture)

### 5.1 Fixture shape and in-code mode selection (sealed)

Two fixture types over a common base: `DirectOpenCodeServerFixture` and
`ContainerOpenCodeServerFixture` (names draft). Tests choose the mode **in code** via TUnit's
`[ClassDataSource<T>(Shared = ...)]` injection — the mode is a visible, per-test-class choice;
no bespoke environment variable exists (sealed: a single global switch was explicitly
rejected). Suites that must run against both modes use an abstract base test class with two
thin `[InheritsTests]` subclasses. Dual-mode is selective rather than a blind duplicate of
the whole operation catalog: process, workspace/filesystem, and stream-sensitive scenarios
run in both modes, alongside basic container health/typed-CRUD smoke; ordinary operation
breadth remains direct-mode unless container behavior can change its outcome. The selected
container suite runs on net8.0, net9.0, and net10.0. The container fixture self-skips via a
TUnit conditional skip attribute when Docker is unavailable or the OS leg cannot run it. If
a global override is ever added it must be platform-native — the channel exists and is
run-verified: MTP discovers
`[AppName].testconfig.json` and TUnit reads arbitrary nested keys from it via
`TestContext.Configuration.Get("harness:mode")`. Tests see only the surface: `Uri Endpoint`,
`OpenCodeClient CreateClient()`, and `Workspace CreateWorkspace()` (§5.4).

**TUnit mechanics run-verified** (2026-08-10 scratchpad spike; TUnit 1.63.25 on
net472/net8.0/net10.0, all green): `SharedType.PerTestSession` creates exactly one fixture
instance per test process — a multi-TFM assembly boots one shared server per TFM leg;
`[InheritsTests]` runs base-declared tests once per concrete subclass; custom conditional
skip works by overriding `SkipAttribute.ShouldSkip(TestRegisteredContext)` (skip lifts when
the condition clears, with the reason reported); `[NotInParallel("group")]` serializes
exactly the keyed group (mechanically asserted via an active-counter) while unconstrained
tests run in parallel; category CI splits work via
`--treenode-filter "/*/*/*/*[Category=X]"`. The `testingPlatform.environmentVariables`
section of testconfig.json is **not relied upon** (did not apply in the spike's MTP
version); the `Configuration.Get` channel is the verified one.

### 5.2 Direct mode

The process is started by **our own launcher** (`OpenCodeServer.StartAsync` — dogfood by
design, §2 principle 4), automatic port (the launcher's `--port=0`/probe chain), binary discovered on
PATH with an in-code fixture option to override. Isolation is a verbatim port of upstream's own
test contract (`cli-process.ts`): `HOME`/`XDG_*`/`OPENCODE_TEST_HOME` redirected to a per-run
temp root, `OPENCODE_DISABLE_PROJECT_CONFIG=1`, `OPENCODE_PURE=1`,
`OPENCODE_DISABLE_AUTOUPDATE=1`, `OPENCODE_DISABLE_AUTOCOMPACT=1`,
`OPENCODE_DISABLE_MODELS_FETCH=1`, `OPENCODE_AUTH_CONTENT={}`, plus `OPENCODE_DISABLE_SHARE=1`
(from upstream's exerciser environment, not `cli-process.ts` — `session.share` otherwise
calls out to the share service), and the whole config injected
inline via `OPENCODE_CONFIG_CONTENT` (the fake LLM provider rides in here, §6). This set fully
decouples tests from any developer-installed opencode state without requiring a container.

### 5.3 Container mode

Testcontainers over **our own image**: a clean-install Dockerfile (base image + opencode's
real installation path, pinned version) — deliberately not upstream's distribution image,
because the clean-install lane exists to prove the path a fresh user takes. The image is
published to **GHCR** (GitHub Container Registry) with a version-pinned tag
(`…/opencode-test:<opencode-version>`); an image build+push workflow runs when the Dockerfile
or the pin changes; tests (CI and local alike) pull the pinned tag. Readiness is port + health
probe (stdout parsing is brittle across a container boundary); teardown rides the container
lifecycle. The per-run workspace root (§5.4) is bind-mounted into the container at a fixed
path (`/workspace`, name draft), pinning the filesystem-namespace difference: tests and server
share one physical directory tree, and the fixture translates between the host view and the
container view. The fake LLM runs host-side; the container reaches it through Testcontainers'
host-port exposure (exact API at build-out, §14) and the `OPENCODE_CONFIG_CONTENT` provider
`baseURL` points at it.

### 5.4 Lifetime and parallelism (sealed)

One shared **server process** per test assembly per mode (TUnit shared fixture;
`PerTestSession` scope is per test *process*, so each TFM leg boots its own server —
spike-verified, §5.1); per-test isolation is **instance-level**. The server multiplexes per-directory Instances — directory
resolution per request is `location[directory]` query > `x-opencode-directory` header > `cwd`
fallback (verified in upstream's location middleware, `packages/server/src/location.ts`) — so
each test creates its own session plus its own **workspace**: a GUID-named subdirectory of a
per-run workspace root, created through the fixture (`Workspace CreateWorkspace()`, name
draft) — tests never hand-build paths. A per-test workspace means a per-test Instance:
instance-scoped mutations (`config.update`, `mcp.add`, `project.update`, `instance.dispose`,
…) are test-local by construction. A workspace exposes two views: `ServerPath` (what rides
the `x-opencode-directory` header) and `HostPath` (where the test seeds and verifies files).
In direct mode the views coincide; in container mode the root is bind-mounted (§5.3) and the
fixture translates.

**The parallelism boundary is process-global state (sealed rule):** a scenario touching only
instance-scoped state runs on the shared process with its own workspace; a scenario touching
process-global state — candidates: `global.*` (config/dispose/upgrade), `auth.set`/
`auth.remove` (the XDG auth store), the `tui.*` request queue, `sync.start` — uses a
dedicated fixture (`DedicatedOpenCodeServerFixture`, name draft), making the need visible in
code (§5.1 philosophy). The candidate list is classified per operation at build-out; the
gate cannot verify this mechanically — convention plus review, recorded honestly. Dedicated
processes also serve: launcher acceptance tests (raw by definition), disconnect/kill
scenarios, and the auth sweep (§7.3, password-enabled instance).

## 6. The fake LLM server

**Decision (sealed): hand-rolled, as a behavior port of upstream's `TestLLMServer`** —
`packages/opencode/test/lib/llm-server.ts` is read as the line-level behavior reference at
build-out; the implementation is ours (Kestrel + Channels idiom, not a transliteration of
Effect-TS). Rejected alternatives: WireMock and the mock-framework field (§3); running
upstream's own `TestLLMServer` via bun (a dependency on upstream's *private test internals* —
the same class of trap that sank `opencode-mcp`, research doc 03).

Shape: Kestrel minimal host on port 0, `POST /v1/chat/completions` **only** — the endpoint the
`@ai-sdk/openai-compatible` test provider actually calls (verified in upstream source:
opencode's whole prompt *and* title path streams through it — `session/llm.ts` `streamText`,
`session/prompt.ts` title via `llm.stream`; upstream's second route, `POST /v1/responses`,
serves its native-`@ai-sdk/openai` test families, which have no counterpart here — extend-only
if a Responses-mode scenario ever appears). Behavior surface ported, sized to our need:

- The scripted reply queue — `EnqueueText`, `EnqueueToolCall`, `EnqueueReasoning`,
  `EnqueueHang`, `EnqueueStreamError`, `EnqueueHttpError(status)`, `EnqueueConnectionReset`
  (names draft) — with per-reply `Usage` (token counts; message usage/cost assertions need
  the fake to produce them); auto-`"ok"` for unqueued requests, a fixed auto-answer for
  title-generation requests, deterministic SSE chunking — the last one **our own addition,
  not an upstream port** (upstream streams queued lines as-is): no delay by default, chunk
  size/delay configurable per reply, to choreograph the cadence of the run opencode
  produces (progressive part/event emission) deterministically.
- The request-side surface: `WaitForRequestsAsync(count)` plus `Hits`/`Inputs` inspection
  (names draft) — the event-bound wait determinism rule 3 (§8.1) requires. Upstream's own
  harness calls `llmWait(1)` after every prompt scenario precisely so in-flight requests
  cannot leak past teardown; the same race exists here on a shared fake.
- Deliberately not ported (upstream has them; no scenario here needs them — extend-only
  candidates): `hold` (promise-gated mid-stream pauses), `raw` (arbitrary/malformed chunk
  injection — tolerance to a broken LLM stream is opencode's contract to test, not ours),
  and the `contentFilter`/`pendingTool` reply variants.

**Scope:** every test that needs assistant activity — the five prompt operations upstream's
harness itself forces onto the fake (`session.prompt`, `session.prompt_async`,
`session.command`, `session.summarize`, `session.init`), the four SSE stream endpoints, and
every flow that reads or manages a run's consequences (messages/parts, the permission and
question flows, revert, the `session.error` event union). It is **not** an SSE-endpoint tool:
most integration scenarios (CRUD, config, fs, catalogs) never touch it, and levels 1–2 never
use it at all.

**Parallelism (sealed):** the queue is global per server, so prompt-dependent tests serialize
in a TUnit `[NotInParallel("llm")]` constraint group; everything else stays parallel.
Content-based request routing stays unported — with the record corrected: upstream *does*
ship it (`pushMatch`/`textMatch`/`toolMatch`; its tool-race test matches replies by content
exactly because reply order there is nondeterministic), but no scenario here produces
order-nondeterministic LLM calls while the `llm` group serializes. If one appears, matching
joins as an extend-only addition.

## 7. Coverage mechanization — "every endpoint exercised"

Upstream's three harness modes translate into this architecture as follows.

### 7.1 Operation inventory (tool-emitted)

`generate` emits, alongside the SDK output, a committed **operation inventory** artifact for
test consumption: operationId, surface (modern/legacy), HTTP method, path template with its
path-parameter names, SSE flag, excluded flag — method and path template exist so the auth
sweep (§7.3) stays a pure loop over this data. It derives
from SpecIR — the tests never parse the spec themselves — and sits under the regen-verify
umbrella, so a spec refresh shows the inventory diff in the PR. (The generator-spec edit this
implies is listed in §13 below.)

### 7.2 Scenario declaration and the coverage gate (sealed)

Integration scenarios declare what they exercise in code —
`[ExercisesOperation("session.get")]` (name draft) on the test method. A gate test diffs
declarations against the inventory:

- **Modern surface (61 ops): a scenario-less operation is a red build.** Hard gate from day
  one of integration build-out.
- **Legacy: best-effort today, consumer-driven tomorrow.** When the MCP server lands, its SDK
  call set is derived mechanically (ADR-0005) and joins the hard gate. The expansion point is
  part of this design, not a future renegotiation.
- **Depth is observed, not declared — the status ledger (staged).** A declaration proves a
  scenario exists, not that it proves anything (upstream's own harness shows the trap: its
  `v2.integration.*` scenarios accept 500s as "exercised"). Stage 2 closes this mechanically:
  a test `DelegatingHandler` on every fixture-created client tallies (method, route template,
  status) across the integration run, and a closing gate test asserts **every modern
  operation observed at least one 2xx** — observation, not declaration. The route-template
  matcher is shared with the sweep (§7.3; the inventory carries method + path template,
  §7.1). Stage 1 (day one, before the scenario suite exists) is the declaration diff alone.
  This is breadth, not a mandate for 61 isolated deep test methods: one real workflow may
  declare and observe several operations. Deep state-building assertions concentrate on the
  risk surfaces (streams, launcher, error mapping, permission/question flows, and stateful
  mutations); low-risk operations still cross the real typed boundary and satisfy the 2xx
  ledger without ceremonial one-test-per-operation expansion.
- **`ErrorPathOnly` is an honest, reasoned exemption.** Operations whose success path
  structurally needs an upstream-private backend (§2 principle 6; day-one candidates: the
  `integration.connect.*` / `integration.attempt.*` family) declare it on the attribute —
  `[ExercisesOperation("…", ErrorPathOnly = "success path needs the real console backend")]`
  — reason mandatory. The gate counts the declaration, exempts the operation from the 2xx
  requirement, and reports the exempt list — visible, never silent.
- Excluded operations (`pty.connect`, both surfaces) are inventory-flagged and gate-exempt.
- `Skip` attributes are forbidden on scenario tests (the same gate test enforces this by
  reflection, whitelisting by attribute type: the container fixture's Docker-unavailable
  conditional skip (§5.1) and `[Quarantined]` (§8.2) are the only sanctioned skip
  mechanisms) — upstream's `--fail-on-skip`. Quarantine (§8) is the only sanctioned
  suppression, and the gate counts quarantined coverage separately: an operation whose *only*
  scenario is quarantined is a gate warning.

### 7.3 Auth + reachability sweep

A mechanical, scenario-less loop over the operation inventory (a TUnit data source — one
reported result per operation, zero per-operation code), run **sequentially** against a
**dedicated** password-enabled server instance: for every operation, (a) without
credentials → expect 401; (b) with credentials → expect non-401 and route-exists (400/422/
404-entity acceptable — proof the route, method, and auth plumbing reached validation).
This is upstream's `auth` mode ported, and it is the piece that breaks the same-source loop
(§2 principle 5) mechanically for all operations on both surfaces: the counterparty is the
real server, not the spec. Mechanics (sealed):

- **Probes ride `SendAsync` + `OpenCodeRoutes`, not typed operation methods.** The sweep
  proves route/method/auth plumbing against the real server; the typed-method→route binding
  is level 2's job (the stub sees the request each typed method produced), so typed calls
  here would duplicate level 2 while demanding mechanical construction of 188 typed inputs.
  Path parameters fill with `auth_*` placeholders (nonexistent entities — upstream's own
  trick); non-GET bodies default to `{}`. The probes still flow through the real client
  pipeline, so auth decoration itself is exercised.
- **A small curated probe table in test code** (upstream's per-scenario `.probe()` ported):
  per-operation optional body/header overrides, `reason` mandatory — upstream itself needed
  `{target: 1}` for `global.upgrade` and a ticket header for `pty.connectToken`.
- **`authOnly` flag** in the same table for destructive parameterless operations
  (`instance.dispose`, `global.dispose`, `sync.start`, …): their credentialed half would
  execute for real and can kill the sweep's own instance, so it is skipped — the 401 half
  still runs, and their reachability is proven by their dedicated-instance scenarios (§5.4).
  Harmless creations (`POST /session` with `{}` creates a session) are accepted on the
  dedicated, disposable instance.
- **SSE operations probe via `ResponseHeadersRead`**: the status arrives with the headers
  and the stream is cancelled immediately — no timeout heuristics (upstream needs a 1 s
  abort race here; we do not).

### 7.4 The `effect` counterpart

The scenario suite itself (§9 and the per-operation scenarios the gate counts) — state-building,
mutation-verifying, stream-consuming depth where it matters.

## 8. Determinism and quarantine policy

### 8.1 Determinism rules (sealed)

1. **No real LLM, no API keys — and no outbound network beyond localhost — anywhere in CI,
   ever.** The fake LLM is the only path, and the isolation env set (§5.2) switches off every
   known outbound call (models fetch, autoupdate, share). This deliberately **corrects the
   ROADMAP assumption** "deterministic runs against free models":
   the upstream verification showed upstream itself never hits a real model in tests (keys
   deleted in preload, no CI secrets) — free models would be cheap but still nondeterministic.
   A live-model suite is likewise rejected (recorded so it is not casually reopened): the SDK's
   subject is transport and typing; real-provider event shapes are upstream's contract to
   test, not ours.
2. **No fixed ports.** Port 0 everywhere — upstream's own lesson, quoted in §1.
3. **No sleep-based synchronization.** Waiting is always event-bound: health probes, expected
   event sequences from the fake LLM (stream tests terminate on "saw the N expected events",
   never on elapsed time), process exit. Timeouts exist as safety nets, never as assertions.
4. **SDK retry is disabled by default in tests** (client options) so error-mapping tests see
   the first response; retry behavior itself is tested explicitly at level 1 against stub
   handlers.

### 8.2 Quarantine (sealed)

- **No blanket retries.** TUnit `[Retry(n)]` only as a justified exception with a comment
  naming the mechanism — the test-culture analog of the analyzer wall's per-rule arbitration.
- A flaky test gets `[Quarantined("https://github.com/…/issues/NNN")]` (name draft) — the
  issue link is a **mandatory attribute parameter** (the tracker is GitHub Issues per
  `docs/agents/issue-tracker.md`). Quarantined tests leave the blocking CI run by category
  filter; a separate **non-blocking** CI step keeps running and reporting them — visible,
  never silently rotting.
- Quarantine is temporary: the attribute is removed when the issue closes. No additional
  tooling (YAGNI).

## 9. Scenario sets

### 9.1 Streams (early exercise — public API spec §11 risk note)

Run against both harness modes (base + `[InheritsTests]`), fake-LLM-backed:

1. **Live stream:** subscribe → trigger a run → observe the expected typed event sequence →
   cancel via token, clean termination.
2. **Durable resume — the critical one:** subscribe → run → collect sequence numbers; then
   re-subscribe with `after` set to a mid-stream cursor → assert gap-free, duplicate-free
   replay.
3. **Disconnect:** on a dedicated instance, sever the connection mid-stream → the failure
   surfaces as an exception from `MoveNextAsync` (public API spec §4.5 tier 1); silent
   termination is a bug.
4. **`session.error` events:** enqueue `httpError`/`streamError` on the fake LLM → assert the
   correctly typed variant of the 8-variant error union and the `ApiError.IsRetryable`
   mapping.
5. **Concurrent streams + a request:** live + durable + a CRUD call simultaneously; on the
   net472 leg this is the standing regression test for the
   `ServicePointManager.DefaultConnectionLimit=2` fix (ROADMAP spike item's test
   counterpart).

**Honest boundary:** unknown-event tolerance cannot be tested at level 3 (a real server cannot
be made to emit unknown tags); it is level 1's job with canned streams (ADR-0009 behavior).

**"Early" is an instruction to `writing-plans`:** these scenarios are written in the same
implementation slice as the SSE engine, not deferred to the end.

### 9.2 Launcher acceptance (ADR-0001)

Own folder in Integration.Tests; the dual-mode harness is **not** used — the launcher is the
SUT. Two groups:

- **Fake-binary tests** (no opencode required; a tiny fake "opencode" script/exe emits
  scripted stdout/exit behavior): readiness-line parsing and its variants; `StartupTimeout` →
  kill + captured output in the exception; immediate exit → fault with captured stderr.
- **Real-binary tests** (the three-OS acceptance itself): default start → `Endpoint` parsed
  and health OK via `CreateClient()`; two concurrent auto-port servers → distinct ports, both
  healthy; explicit port conflict → fail-loud; `DisposeAsync` → the whole process tree is
  gone, no orphans; `Password`/`Config` → correct env injection and `CreateClient()`
  auto-auth.
- **Orphan protection** (Job Object): testable only via a helper-process technique (a helper
  starts the launcher then dies; the test asserts opencode died too). The design records the
  mechanism; depth belongs to the launcher deep-dive (public API spec §13).

Running these on all three OS legs satisfies ADR-0001's acceptance criterion.

## 10. Tooling tests (generator spec §11 — sealed, with three revisions)

The §11 sketch is sealed as designed: projection quirk fixtures — loaded through the pinned
Microsoft.OpenApi reader, whose internals are never re-tested — with the no-count-assertions rule,
binder red tests verified inside the batched categorized report, per-emitter Verify
micro-snapshots over small EmitPlan fixtures, Writer/command tests on MockFileSystem +
faked `Infrastructure` wrappers + `CommandAppTester`, the ingestion tripwire and DOM-boundary guard tests (generator spec §4.1/§11),
and the double-emit determinism test.
The compile-gate item stays untouched (architecture, generator spec §13's consequence — not
this session's to revisit). Revisions:

1. **New tool outputs get tests.** This design added two tool responsibilities (operation
   inventory §7.1, contract fixtures §3 level 2): Tools.Tests covers inventory fidelity to
   curation (exclusions absent, SSE flagged) and fixture-synthesis determinism by direct byte
   comparison.
2. **`refresh-spec` command tests** were missing from the sketch: faked git/copy wrappers,
   `SNAPSHOT.md` rewrite, diff-summary output — `CommandAppTester` + MockFileSystem.
3. **Placement clarification:** §11's "round-trip behavior" tests (known tag → variant,
   unknown tag → carrier + re-serialization, out-of-order discriminators, explicit-null vs
   missing, guarded getter / guarded `PrintMembers`) are product tests and land in
   `OpenCode.Sdk.Tests` (level 1), not Tools.Tests.

Dependency note: Verify.TUnit is centrally pinned for the tooling snapshot tests.

## 11. CI architecture

Extends the existing `ci.yml` (three-OS build + test + TRX + dorny + artifacts, Linux-only
format gate, and the generator spec §13 `generate --verify` step):

1. **Three-OS legs grow:** install opencode on the runner (npm, **pinned version**) and run
   Integration.Tests in direct mode. The pin is **single-sourced** with the spec pin family
   and **machine-readable**: `refresh-spec` stamps `spec/opencode-version` — a single-line
   text file (extended only if a second pin value ever materializes) — alongside the
   human-read `spec/SNAPSHOT.md`, which relays it. Its consumers: the three-OS install
   steps, the image build workflow (tag + build arg), the container fixture (which tag to
   pull), and the canary's pin-vs-latest report. Invariant made mechanical: *an SDK
   generated from spec vX is tested against server vX.*
2. **New Linux container legs:** pull the pinned GHCR image and run the selective
   container-mode suite (the clean-install lane) on **net8.0, net9.0, and net10.0**;
   net472 is Windows-only and has no container leg. The image
   build+push workflow is separate and runs only when the Dockerfile or the pin changes —
   no scheduled rebuilds and no tag cleanup (public GHCR, YAGNI): the image refreshes
   naturally on every pin bump, i.e. at the refresh cadence itself.
3. **Quarantine step:** non-blocking, category-filtered (§8.2).
4. **Nightly canary (non-blocking):** a scheduled job installs **unpinned** `opencode@latest`
   and runs the integration suite — read-only CI signal, no deployment anywhere; it answers
   "does our shipped SDK still work against what consumers actually install today".
   Rationale: upstream ships hourly betas; the fingerprint radar only sees the spec at
   refresh time — the canary surfaces *behavioral* drift between refreshes, and normal CI
   never can (it only ever sees the pin — exactly how the reference repo's pinned CI missed
   a breaking change for 70 days, per its own `aws-sdk-canary.yml` header). **Ownership is
   the issue tracker (sealed):** on failure the job files a `canary`-labeled GitHub issue,
   or comments the run link onto the already-open one (label-deduped) — the non-blocking
   signal lands durably where triage already looks (`docs/agents/issue-tracker.md`) instead
   of rotting in the Actions tab.
5. **TRX/dorny extends** to the new legs with distinct leg names. Badges are out of scope
   (YAGNI; the BadgeSmith flow can be ported later if wanted).
6. **Local dev loop:** default experience is direct mode; fast inner loop is the unit+contract
   categories under MTP (`mtp-hot-reload` for red-test iteration, per ROADMAP); container
   tests self-skip without Docker.
7. **Duration guardrail (recorded, no speculative trimming):** if an integration leg exceeds
   ~15 minutes, the first trim candidate is reducing the middle TFMs (net8.0/net9.0) to a
   smoke scenario set — the decision is taken then, with measurements; the all-TFMs seal (§4)
   is not renegotiated from scratch.

## 12. Fixtures, snapshots, coverage philosophy

- **Verify has exactly two uses**: emitter micro-snapshots (§10) and the **public API surface
  lock** — a PublicApiGenerator-style surface dump per package under Verify approval,
  which turns any member removal or signature change into a reviewable diff (the ROADMAP
  queue-1 `api-design`/`snapshot-testing` intent made concrete). Behavior tests never use
  snapshots — assertion intent stays explicit in the test body.
- **Tool-emitted test artifacts are committed** (operation inventory, contract fixtures) and
  tracked as a second output root of the tool's manifest/regen-verify machinery — hand-editing
  is structurally excluded, drift is loud. Hand-written fixtures (projection quirk specs, canned
  SSE streams) stay small, one per quirk, in the owning test project's `Fixtures/` folder. LF
  is already enforced repo-wide.
- **Coverage philosophy: risk-focused, no numeric gate** (sealed). The structural mechanisms
  already force meaningful coverage (level 2 = 100% of operations, §7.2 gate = 100% of the
  modern surface, the analyzer wall, on-merit generated code); a line-coverage threshold would
  mostly measure generated-code volume. Coverage reports are collected as CI artifacts for
  visibility, thresholds none; the hand-written core (transport, SSE engine, launcher) gets a
  periodic manual CRAP-style risk review; generated code is excluded from coverage metrics.

## 13. Cross-document impact (each edit lands with its own maintainer approval)

- **ROADMAP:** testing design done (queue 1 closes into `writing-plans`); the "Testing
  strategy details" open question resolves to this spec; the "free models" wording is
  corrected per §8.1; the Extensions/integration test-project additions in queue 2 now follow
  §4.
- **Generator spec:** the tool's output list gains the operation inventory (§7.1 — including
  its HTTP method and path-template fields) and the contract fixtures (§3); §11 becomes a
  pointer to this spec (its caveat anticipated exactly this); §10's refresh-spec flow names
  the machine-readable pin file (§11.1).
- **`spec/SNAPSHOT.md` family:** `refresh-spec` additionally stamps the opencode test-server
  pin into `spec/opencode-version`, relayed by `SNAPSHOT.md` (§11.1).

## 14. UNVERIFIED / build-out items

- **Testcontainers .NET host-port exposure** API shape for the fake-LLM reachability, and the
  bind-mount API shape for the workspace root (§5.3).
- **GHCR anonymous pull** for the public repo's image (expected to work; verify before wiring
  the container leg).
- **opencode installation path** for CI runners and the Dockerfile (npm package name,
  installer script, version pinning syntax) — resolved when the CI legs are built.
- **Health endpoint choice** for container readiness probing (modern `global.health` vs a
  cheaper TCP-only check).
- **Fake-binary launcher test technique** portability (script vs tiny compiled helper) across
  the three OS legs. Reference for its scripted stdout: upstream's own readiness parse is
  `/listening on (http:\/\/([^\s:]+):(\d+))/` (`cli-process.ts`) — the fake binary replays
  these line shapes and their failure variants.
- **Windows filewatcher flake knob (informational):** upstream disables the server's file
  watcher on its Windows CI and in the desktop WSL sidecar
  (`OPENCODE_EXPERIMENTAL_DISABLE_FILEWATCHER=true`, a documented env var) — watcher handles
  on watched directories are a known Windows teardown-EBUSY/flake source. Not in our default
  env set (it changes server behavior: watcher-driven events stop); it is the first knob to
  reach for if the Windows integration leg flakes on workspace cleanup.
