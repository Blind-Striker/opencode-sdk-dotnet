# Testing Architecture & Strategy — OpenCode.Sdk, OpenCode.Sdk.Extensions, tools/

Date: 2026-08-10

Design specification produced by the testing-architecture brainstorm session (2026-08-09/10,
ROADMAP queue item 1 — its final design step). Every decision below was discussed and sealed
individually with the maintainer. This spec owns the ROADMAP Open Questions item "Testing
strategy details". Process sequencing agreed: an optional grill session stress-tests this spec;
then `writing-plans` produces the multi-phase implementation plan (vertical slices co-developing
`tools/`, SDK, Extensions, and tests).

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
   launcher. The two modes differ in exactly one variable — process management — so
   "direct red, container green" indicts the launcher (§5).
5. **Same-source circularity is acknowledged, not hidden.** Level 2's fixtures and the
   generator derive from the same spec: a misread spec produces models and stubs that agree
   with each other. Only level 3 (and its mechanical sweep, §7.3) breaks the loop.

## 3. Test levels (the backbone)

| Level | Proves | Runs against | Lives in |
|---|---|---|---|
| **1 — Unit** | Internal logic of our code: converters, envelope guards, retry, SSE engine parsing, launcher pieces; tools parser/binder/emitters; Extensions DI composition | Nothing external — in-memory `HttpMessageHandler` stubs, canned payloads and streams | `OpenCode.Sdk.Tests`, `OpenCode.Sdk.Extensions.Tests`, `OpenCode.Sdk.Tools.Tests` |
| **2 — Contract** | Every operation's happy path + every declared error response maps to the right model/envelope/exception — mechanical 100% endpoint breadth | Tool-generated fixtures (from SpecIR) fed through the same in-memory stub handler | `OpenCode.Sdk.Tests` (`Contract/` area) |
| **3 — Integration** | The SDK works for real: real HTTP, real serialization boundary, real SSE over the network, real process lifecycle | A real `opencode serve` via the dual-mode harness (§5), fake LLM where assistant activity is needed (§6) | `OpenCode.Sdk.Integration.Tests` |

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
| OpenCode.Sdk.Integration.Tests | net472 (Windows-only) + net8.0/net9.0/net10.0 — **all TFMs, maintainer decision** | 3 OS direct + Linux container |

Rationale notes: the contract area shares the unit project because its dependency set is
identical (no process, stub handler, fixture JSON) — separation is by folder/namespace and
test category, matching the reference repos' process/no-process split. Integration is a
separate project because its run profile differs (duration, opencode-installed precondition,
distinct CI gating). netstandard2.0 has no runtime; the net472 legs are its proxy coverage
(ADR-0002) — the integration net472 leg is the *behavioral* half of that claim
(`HttpWebRequest`-based handler stack, `ServicePointManager` limits, long-lived SSE on
Framework). Launcher acceptance tests live inside Integration.Tests in their own folder and do
**not** use the dual-mode harness — the launcher is the SUT there (§9.2). The fake LLM fixture
lives under Integration.Tests (`Support/`) until a second consumer forces extraction (YAGNI).

## 5. The real-process harness (dual-mode fixture)

### 5.1 Fixture shape and in-code mode selection (sealed)

Two fixture types over a common base: `DirectOpenCodeServerFixture` and
`ContainerOpenCodeServerFixture` (names draft). Tests choose the mode **in code** via TUnit's
`[ClassDataSource<T>(Shared = ...)]` injection — the mode is a visible, per-test-class choice;
no bespoke environment variable exists (sealed: a single global switch was explicitly
rejected). Suites that must run against both modes use an abstract base test class with two
thin `[InheritsTests]` subclasses. The container fixture self-skips via a TUnit conditional
skip attribute when Docker is unavailable or the OS leg cannot run it. If a global override is
ever added it must be platform-native (MTP `testconfig.json` is the candidate) — mechanism
verification at build-out (§14). Tests see only the surface: `Uri Endpoint` +
`OpenCodeClient CreateClient()`.

### 5.2 Direct mode

The process is started by **our own launcher** (`OpenCodeServer.StartAsync` — dogfood by
design, §2 principle 4), automatic port (the launcher's `--port=0`/probe chain), binary discovered on
PATH with an in-code fixture option to override. Isolation is a verbatim port of upstream's own
test contract (`cli-process.ts`): `HOME`/`XDG_*`/`OPENCODE_TEST_HOME` redirected to a per-run
temp root, `OPENCODE_DISABLE_PROJECT_CONFIG=1`, `OPENCODE_PURE=1`,
`OPENCODE_DISABLE_AUTOUPDATE=1`, `OPENCODE_DISABLE_AUTOCOMPACT=1`,
`OPENCODE_DISABLE_MODELS_FETCH=1`, `OPENCODE_AUTH_CONTENT={}`, and the whole config injected
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
lifecycle. The fake LLM runs host-side; the container reaches it through Testcontainers'
host-port exposure (exact API at build-out, §14) and the `OPENCODE_CONFIG_CONTENT` provider
`baseURL` points at it.

### 5.4 Lifetime and parallelism (sealed)

One shared server per test assembly per mode (TUnit shared fixture); per-test isolation is
**session-level** — each test creates its own session, per-test temp directories ride the
`x-opencode-directory` header. Exceptions spawn their own dedicated process: launcher
acceptance tests (raw by definition), disconnect/kill scenarios, destructive instance-level
operations, and the auth sweep (§7.3, password-enabled instance).

## 6. The fake LLM server

**Decision (sealed): hand-rolled, as a behavior port of upstream's `TestLLMServer`** —
`packages/opencode/test/lib/llm-server.ts` is read as the line-level behavior reference at
build-out; the implementation is ours (Kestrel + Channels idiom, not a transliteration of
Effect-TS). Rejected alternatives: WireMock and the mock-framework field (§3); running
upstream's own `TestLLMServer` via bun (a dependency on upstream's *private test internals* —
the same class of trap that sank `opencode-mcp`, research doc 03).

Shape: Kestrel minimal host on port 0, `POST /v1/chat/completions` + `POST /v1/responses`
(upstream's fake serves both; we stay provider-agnostic). Behavior surface ported: a scripted
reply queue — `EnqueueText`, `EnqueueToolCall`, `EnqueueReasoning`, `EnqueueHang`,
`EnqueueStreamError`, `EnqueueHttpError(status)`, `EnqueueConnectionReset` (names draft) —
auto-`"ok"` for unqueued requests, a fixed auto-answer for title-generation requests,
deterministic SSE chunking (no delay by default; chunk size/delay configurable per reply).

**Scope:** every test that needs assistant activity — the five prompt operations upstream's
harness itself forces onto the fake (`session.prompt`, `session.prompt_async`,
`session.command`, `session.summarize`, `session.init`), the four SSE stream endpoints, and
every flow that reads or manages a run's consequences (messages/parts, the permission and
question flows, revert, the `session.error` event union). It is **not** an SSE-endpoint tool:
most integration scenarios (CRUD, config, fs, catalogs) never touch it, and levels 1–2 never
use it at all.

**Parallelism (sealed):** the queue is global per server, so prompt-dependent tests serialize
in a TUnit `[NotInParallel("llm")]` constraint group; everything else stays parallel.
Content-based request routing was considered and rejected as complexity without precedent —
upstream relies on queue discipline the same way.

## 7. Coverage mechanization — "every endpoint exercised"

Upstream's three harness modes translate into this architecture as follows.

### 7.1 Operation inventory (tool-emitted)

`generate` emits, alongside the SDK output, a committed **operation inventory** artifact for
test consumption: operationId, surface (modern/legacy), SSE flag, excluded flag. It derives
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
- Excluded operations (`pty.connect`, both surfaces) are inventory-flagged and gate-exempt.
- `Skip` attributes are forbidden on scenario tests (the same gate test enforces this by
  reflection) — upstream's `--fail-on-skip`. Quarantine (§8) is the only sanctioned
  suppression, and the gate counts quarantined coverage separately: an operation whose *only*
  scenario is quarantined is a gate warning.

### 7.3 Auth + reachability sweep

A mechanical, scenario-less loop generated from the inventory, run against a **dedicated**
password-enabled server instance: for every operation, through the SDK, (a) without
credentials → expect 401; (b) with credentials → expect non-401 and route-exists (400/422/
404-entity acceptable — proof the route, method, and auth plumbing reached validation).
This is upstream's `auth` mode ported, and it is the piece that breaks the same-source loop
(§2 principle 5) mechanically for all operations on both surfaces: the counterparty is the
real server, not the spec.

### 7.4 The `effect` counterpart

The scenario suite itself (§9 and the per-operation scenarios the gate counts) — state-building,
mutation-verifying, stream-consuming depth where it matters.

## 8. Determinism and quarantine policy

### 8.1 Determinism rules (sealed)

1. **No real LLM, no API keys, anywhere in CI — ever.** The fake LLM is the only path. This
   deliberately **corrects the ROADMAP assumption** "deterministic runs against free models":
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
5. **`session.history` numeric `after`/`limit`:** this scenario is the **designated catch
   point** for the behavior-premised curation overrides (generator spec §5.3 assigns that
   residual risk to integration tests) — if the `parameterTypeOverrides` premise drifts, this
   test reddens.
6. **Concurrent streams + a request:** live + durable + a CRUD call simultaneously; on the
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

The §11 sketch is sealed as designed: parser quirk fixtures with the no-count-assertions rule,
binder red tests verified inside the batched categorized report, per-emitter Verify
micro-snapshots over small EmitPlan fixtures, Writer/command tests on MockFileSystem +
faked `Infrastructure` wrappers + `CommandAppTester`, and the double-emit determinism test.
The compile-gate item stays untouched (architecture, generator spec §13's consequence — not
this session's to revisit). Revisions:

1. **New tool outputs get tests.** This design added two tool responsibilities (operation
   inventory §7.1, contract fixtures §3 level 2): Tools.Tests covers inventory fidelity to
   curation (exclusions absent, SSE flagged) and fixture-synthesis determinism (snapshots).
2. **`refresh-spec` command tests** were missing from the sketch: faked git/copy wrappers,
   `SNAPSHOT.md` rewrite, diff-summary output — `CommandAppTester` + MockFileSystem.
3. **Placement clarification:** §11's "round-trip behavior" tests (known tag → variant,
   unknown tag → carrier + re-serialization, out-of-order discriminators, explicit-null vs
   missing, guarded getter / guarded `PrintMembers`) are product tests and land in
   `OpenCode.Sdk.Tests` (level 1), not Tools.Tests.

Dependency note: Verify (Verify.TUnit) is not yet in `Directory.Packages.props` — added at
build-out.

## 11. CI architecture

Extends the existing `ci.yml` (three-OS build + test + TRX + dorny + artifacts, Linux-only
format gate, and the generator spec §13 `generate --verify` step):

1. **Three-OS legs grow:** install opencode on the runner (npm, **pinned version**) and run
   Integration.Tests in direct mode. The pin is **single-sourced** with the spec pin family:
   `refresh-spec` stamps the test-server version alongside `spec/SNAPSHOT.md`, and the
   container image tag consumes the same pin. Invariant made mechanical: *an SDK generated
   from spec vX is tested against server vX.*
2. **New Linux container leg:** pulls the pinned GHCR image, runs Integration.Tests in
   container mode (the clean-install lane). The image build+push workflow is separate and
   runs only when the Dockerfile or the pin changes.
3. **Quarantine step:** non-blocking, category-filtered (§8.2).
4. **Nightly canary (non-blocking):** a scheduled job installs **unpinned** `opencode@latest`
   and runs the integration suite. Rationale: upstream ships hourly betas; the fingerprint
   radar only sees the spec at refresh time — the canary surfaces *behavioral* drift between
   refreshes. Direct precedent: the maintainer's own `aws-sdk-canary.yml`
   (localstack-dotnet-client), whose header records that pinned CI once missed a breaking
   change for 70 days.
5. **TRX/dorny extends** to the new legs with distinct leg names. Badges are out of scope
   (YAGNI; the BadgeSmith flow can be ported later if wanted).
6. **Local dev loop:** default experience is direct mode; fast inner loop is the unit+contract
   categories under MTP (`mtp-hot-reload` for red-test iteration, per ROADMAP); container
   tests self-skip without Docker.

## 12. Fixtures, snapshots, coverage philosophy

- **Verify has exactly two uses** (sealed): emitter micro-snapshots (§10) and the **public API
  surface lock** — a PublicApiGenerator-style surface dump per package under Verify approval,
  which turns any member removal or signature change into a reviewable diff (the ROADMAP
  queue-1 `api-design`/`snapshot-testing` intent made concrete). Behavior tests never use
  snapshots — assertion intent stays explicit in the test body.
- **Tool-emitted test artifacts are committed** (operation inventory, contract fixtures) and
  tracked as a second output root of the tool's manifest/regen-verify machinery — hand-editing
  is structurally excluded, drift is loud. Hand-written fixtures (parser quirk specs, canned
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
- **Generator spec:** the tool's output list gains the operation inventory and the contract
  fixtures (§7.1, §3); §11 becomes a pointer to this spec (its caveat anticipated exactly
  this).
- **`spec/SNAPSHOT.md` family:** `refresh-spec` additionally stamps the opencode test-server
  pin (§11.1).
- **Handover:** `docs/agents/handover-prompts/HANDOFF-2026-08-09-testing-session.md` is
  deleted when this session's outcome ships.

## 14. UNVERIFIED / build-out items

- **TUnit mechanics** (all believed supported, none run-verified yet): `ClassDataSource`
  shared-instance semantics across a multi-TFM assembly, `[InheritsTests]` dual-run shape,
  custom conditional-skip attributes, `[NotInParallel("group")]` constraint groups, MTP
  `testconfig.json` as the global-override channel, category filtering syntax for the CI
  splits. First build-out step of the integration project is a thin spike proving these.
- **Testcontainers .NET host-port exposure** API shape for the fake-LLM reachability (§5.3).
- **GHCR anonymous pull** for the public repo's image (expected to work; verify before wiring
  the container leg).
- **opencode installation path** for CI runners and the Dockerfile (npm package name,
  installer script, version pinning syntax) — resolved when the CI legs are built.
- **Health endpoint choice** for container readiness probing (modern `global.health` vs a
  cheaper TCP-only check).
- **Verify.TUnit** package addition to `Directory.Packages.props`.
- **Fake-binary launcher test technique** portability (script vs tiny compiled helper) across
  the three OS legs.
