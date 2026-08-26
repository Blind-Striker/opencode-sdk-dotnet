# Continuous Protocol Coverage Program Design

Date: 2026-08-26

> **Status: maintainer-approved design; execution has not started.** Current architecture,
> engineering canon, and accepted ADRs remain authoritative until the decision changes proposed
> here are separately reviewed and landed. This document is a program design, not an implementation
> plan, current operational status, or permission to move the protocol pin.

## 1. Purpose

The SDK will cover the accepted opencode `ClientApi` surface completely, stay aware of a rapidly
moving upstream branch without generating from a floating reference, and prove compatibility through
deterministic tests against the exact server paired with the accepted protocol snapshot.

The program has three independent claims:

1. **Surface:** every operation in the target public ledger is callable through a reviewed .NET
   interface.
2. **Continuity:** upstream drift is observed continuously, while accepted source and public API move
   only through an exact, human-reviewed snapshot receipt.
3. **Assurance:** deterministic contract tests and exact-pin real-server tests prove distinct failure
   classes; neither is reported as a substitute for the other.

The evidence forcing this redesign is recorded in
[`docs/research/21-openapi-projection-fidelity.md`](../../research/21-openapi-projection-fidelity.md):
endpoint and declared-channel projection are mechanically faithful, while projection bugs,
structural information loss, and server behavior absent from the Effect contract prevent an
unmodified OpenAPI document from describing every functional detail of the SDK.

## 2. Program Vocabulary

### Accepted snapshot

One reviewed protocol identity consisting of:

- an exact upstream git commit;
- a normalized `spec/openapi.json` digest;
- an ordered snapshot recipe whose patch list is normally empty;
- a sorted operation-set digest; and
- the matching upstream submodule gitlink.

No branch name, package channel, operation count, or current-tip observation is an accepted identity.

### Contract inventory ledger

The complete operation set in the accepted normalized `ClientApi` OpenAPI document. It records
selected, pending, and transport-owned operations without silently deleting any of them.

### Target public surface ledger

The subset the current product decision requires to be callable. Its default is the complete contract
inventory: an operation consumed by any first-party client is target surface, and no deferral
classification exists in this program cycle. Narrowing the target requires a future
maintainer-approved amendment of the coverage-ledger decision.

### Admission coverage

Callable target operations divided by the target public surface ledger. Contract inventory coverage
is reported separately. A single percentage never combines these claims.

## 3. Surface Ownership

### 3.1 Default generated surface

Ordinary HTTP, SSE, text, and binary operations are generated as short delegations into the
hand-written behavior core. Generated ownership includes:

- public clients, handles, request records, response envelopes, and wire models;
- internal routes, response/stream adapters, serializer metadata, and status verdicts; and
- deterministic operation inventory rows.

The generator emits only shapes represented by the accepted normalized OpenAPI document. Unsupported
shapes continue to fail closed.

### 3.2 WebSocket operations

A WebSocket operation is not represented honestly by an ordinary generated `Task<bool>` HTTP method.
The accepted OpenAPI operation remains generator-owned as inventory, route/query metadata, and an
exclusion fingerprint. Actual upgrade orchestration, framing, cancellation, and disposal are
hand-written runtime behavior.

Product coverage is complete only when an accepted WebSocket operation has a callable hand-written
door. Generator exclusion alone is not product coverage.

### 3.3 Normal PTY ownership

Normal PTY is a deliberate family-specific ownership exception:

- all public `PtysClient`, `PtyClient`, token, and connection doors are hand-written;
- the existing collection/handle shape remains because a PTY is a working object;
- `PtySession` is a separate live working object that owns a WebSocket connection;
- internal raw clients/operation descriptors, routes, query shapers, adapters, status verdicts,
  public response envelopes, public wire models, and serializer metadata remain generator-owned;
- the token sentinel is applied internally and never supplied by the caller;
- the raw token capability remains callable through a safe public door; and
- the family may not bypass generic envelope machinery for list or other represented responses.

The public interface is therefore coherent and hand-written while compile-coupled internal generated
artifacts preserve route/status/schema drift locality.

Illustrative use:

```csharp
var pty = client.Ptys.GetPtyClient(ptyId);

await using var session = await pty.ConnectAsync(
    new PtyConnectOptions { Cursor = cursor },
    cancellationToken);

await session.WriteAsync("dotnet test\n", cancellationToken);

await foreach (var frame in session.ReadAsync(cancellationToken))
{
    // Terminal output, replay cursor, or completion.
}
```

This design does not introduce a generated `websocket-protocols.json` contract. The frame protocol is
explicit hand-written runtime behavior, tested through named fixtures and the exact pinned server.
Upstream operation-subtree fingerprints retain OpenAPI drift visibility.

### 3.4 Persistent PTY posture

Persistent PTY is ordinary target surface. Its eight HTTP operations land as a normal generated batch
once the accepted refresh and the operation-identity curation rows admit them; `persistentPty.connect`
is a transport-owned WebSocket operation whose hand-written session door is sequenced after the
normal-PTY session machinery exists and reuses it. Exact-pin success observations require the external
`opencode-pty` daemon; where CI cannot obtain it, the affected operations carry named exemptions
pinning the declared 503 `ServiceUnavailableError` as the observed contract.

## 4. Snapshot Production

### 4.1 Normal mode

The normal path consumes the exact committed upstream OpenAPI artifact without modification:

```text
exact upstream SHA -> raw artifact -> normalized artifact (identity transform)
```

The patch list is empty, raw and normalized bytes are identical, and upstream generation is not run
merely to copy the document.

### 4.2 Temporary repair mode

The escape hatch applies an ordered, hash-verified source patch to a detached upstream worktree and
runs the exact pinned upstream generator:

```text
exact upstream SHA
  -> raw committed artifact
  -> baseline generated artifact
  -> ordered temporary source patches
  -> normalized generated artifact
  -> structural and byte invariants
  -> immutable review receipt
```

The only allowed patch class:

- **Restore:** recover information present in the upstream machine-readable contract but lost by its
  projection, such as the SSE payload links addressed by
  [anomalyco/opencode#45182](https://github.com/anomalyco/opencode/pull/45182).

Identity and naming defects — for example, operationIds missing the `v2.` convention — are never
patched: they are admitted through reason-bearing operation-identity curation rows carrying the
upstream issue reference and the expected corrected identity, and they retire loudly when upstream's
fix makes the rows stale. Contract-content loss is patched; identity defects are curated; both are
reported upstream.

Forbidden patch class:

- **Enrich:** auth behavior, location headers, fixed PTY ticket values, WebSocket frame semantics,
  undeclared process errors, or any other fact learned only from server implementation. These remain
  explicit hand-written runtime behavior and assurance evidence.

Every active patch requires an upstream issue/PR, exact byte hash, ordered position, touched-file
preimages, repair predicates, and a retirement predicate. If raw upstream satisfies the repair
predicate, the synchronizer refuses to apply the patch and requires an empty-patch retirement refresh.

The desired lifecycle is `empty -> temporary repair -> empty`, not a growing local fork.

### 4.3 Synchronizer interface

The planned repository-tool module exposes three operations:

```text
opencode-tool refresh-spec --ref <sha-or-moving-ref>
opencode-tool refresh-spec --verify
opencode-tool refresh-spec --apply <receipt.json>
```

- Prepare resolves a moving reference once, uses the resulting full SHA everywhere, writes only
  scratch artifacts, and changes no accepted repository file.
- Verify reproduces the accepted recipe observationally and never repairs product files.
- Apply accepts one reviewed receipt, refuses time-of-check/time-of-use drift, updates only known
  snapshot/generated paths, and never stages, commits, or pushes.

The module owns git worktrees, patch application, upstream process execution, artifact hashing,
OpenAPI delta walls, candidate generator probes, receipts, and rollback. It remains separate from
`GenerationCoordinator`, whose interface continues to compile one accepted spec into SDK output.

### 4.4 Continuous observation, deliberate acceptance

Upstream tracking never means generating from floating HEAD in ordinary builds:

| Lane | Identity | Mutates accepted state | Initial role |
|---|---|---:|---|
| Pinned integrity | Accepted SHA/recipe | No | Blocking on every relevant PR/push |
| Tip detector | Resolved current `v2` SHA | No | Scheduled read-only drift signal |
| Candidate refresh | One resolved SHA | No | Periodic complete receipt and PublicApi diff |
| Accepted refresh | Reviewed receipt | Yes, locally only | Maintainer decision |
| Latest behavioral canary | Resolved current `v2` source SHA | No | Non-blocking compatibility signal |

Initial cadence is daily observation, weekly candidate preparation, and nightly behavioral canary.
Frequency may increase after the lanes are cheap and stable. Once prepare/verify/apply exists, the
standing policy is a refresh to the latest upstream commit (plus active Restore patches) at the start
of every working session, each through its own reviewed receipt; until that tooling lands, refreshes
remain deliberate maintainer acts. Every lane executes upstream from git source at its resolved SHA
through a pinned bun toolchain with install scripts disabled; no lane installs or runs upstream npm
artifacts. Automation may open or update local issues, but it does not alter the pin, curation,
generated output, releases, or upstream issues.

## 5. Location and Request Options

`OpenCodeRequestOptions` gains typed per-call `LocationSelector? Location`. No arbitrary header
dictionary is introduced.

Resolution semantics:

```text
operation location query
  > per-call location headers
  > client-lifetime ambient location headers
  > server cwd
```

Per-call values override ambient values member by member, mirroring the server's own per-member
resolver; an unset per-call member inherits the ambient member, so a caller cannot clear an ambient
member for one call. Resolved headers are injected uniformly with no route branching — session-scoped
routes derive location from the session row, and the option documents that no-op. Header encoding
mirrors the server's asymmetry: the directory header is percent-encoded, the workspace header rides
raw and is omitted when absent. The server remains responsible for query precedence. The fixed PTY
ticket header is unrelated and remains internal to the PTY implementation.

The existing two SSE operations do not require per-call location options, so stream signatures remain
unchanged. When a stream operation first carries a meaningful per-call member, it receives a dedicated
stream options type without an error-behavior member, so the compiler keeps refusing what a stream
cannot answer. Existing request-building tests gain focused per-call/ambient precedence cases; exact-pin
integration and latest canary tests provide server-agreement evidence. No source-fingerprint registry
is added solely for location behavior.

## 6. Envelope Completion

The envelope binder reuses the generator's existing type machinery instead of requiring every
payload to be a direct named reference.

An envelope payload is accepted when its ingested `SchemaNode` can already bind to a supported
`TypeReferencePlan`, including:

- named models;
- lists;
- dictionaries;
- promoted inline object models; and
- represented nullable payloads.

Unsupported schema nodes still fail closed. Family-specific array/dictionary/inline exceptions are
forbidden.

`EnvelopePlan` carries a type reference rather than only a type-name string. DTO/envelope emitters use
the existing `TypeSyntaxEmitter`. A successful nullable payload is distinguished from an error path by
response state, not by treating `null` as an unset backing field. Inline models require deterministic
operation-scoped naming or a reasoned naming row.

## 7. Assurance Architecture

### 7.1 Independent assurance planes

Report these independently:

1. contract inventory and target admission;
2. deterministic success-contract obligations;
3. deterministic distinct error obligations;
4. exact-pin real-server typed success observations;
5. named exact-pin exemptions;
6. SSE/WebSocket transport scenarios;
7. launcher platform scenarios;
8. latest-canary identity and age; and
9. optional agent-driven real-provider evidence.

No single percentage combines these claims.

### 7.2 TUnit integration fixture

The exact-pin server fixture follows TUnit shared-resource mechanics:

```csharp
public sealed class PinnedOpenCodeServerFixture :
    IAsyncInitializer,
    IAsyncDisposable
{
    public Uri Endpoint { get; }
    public OpenCodeClient CreateClient();
    public TestWorkspace CreateWorkspace();
}
```

```csharp
[ClassDataSource<PinnedOpenCodeServerFixture>(
    Shared = SharedType.PerTestSession)]
public sealed class SessionIntegrationTests(
    PinnedOpenCodeServerFixture server)
{
}
```

The fixture initially uses a test-only CliWrap adapter:

- pull-based stdout/stderr events continuously drain both streams;
- the started event records the process ID;
- JSON readiness supplies the endpoint; the fixture generates the lease credential itself and
  injects it through the child environment (`OPENCODE_PASSWORD`) — stdio mode never prints it;
- the server binds port zero;
- stdin remains open as the ownership lease;
- teardown closes stdin, waits a bounded graceful interval, then terminates forcefully;
- logs and identities are retained on failure; and
- each test owns an isolated workspace under a per-run home/state root.

CliWrap is not a shipped SDK dependency. Process-global/destructive scenarios use a keyed
`NotInParallel` constraint or a dedicated fixture. Ordinary workspace-scoped scenarios remain
parallel.

### 7.3 Launcher relationship

M4 is redesigned against current upstream behavior: `serve --stdio --port 0`, JSON readiness, a
caller-supplied lease credential, continuous output drain, stdin-EOF ownership, and bounded tree
termination.

Launcher-focused integration tests dogfood production `OpenCodeServer.StartAsync`. The raw test-only
fixture remains a control adapter where separating launcher failures from SDK/server agreement adds
diagnostic value. Process management must not be copied into every scenario.

### 7.4 Deterministic model/session workflow

A blocking repository integration test uses the same-commit upstream simulation mechanism. It starts
the exact pinned server, opens real SDK streams, creates a real session, sends a real prompt, scripts
deterministic provider chunks, observes typed events, reads persisted messages through generated
operations, and proves cancellation/cleanup.

The test uses no provider credentials and permits no unregistered outbound network. It proves the
real opencode session/provider/event/persistence path without making nondeterministic model behavior a
release gate.

The controller answering the simulation's WebSocket JSON-RPC channel is a small repository-owned C#
client in the shared test infrastructure, so chunk scripts live beside the assertions; the exact-pin
pairing freezes the protocol it speaks, and running the server from source keeps the simulation
package present.

### 7.5 Latest canary

A non-blocking scheduled lane runs a stable compatibility subset and the deterministic session story
against upstream run from git source at the resolved current `v2` tip SHA. It records the exact
observed identity and
opens or updates one deduplicated owned issue on semantic failure. It never regenerates, moves the
pin, publishes, or repairs code.

### 7.6 Real provider/model acceptance

Real-provider/model acceptance is not part of the repository test suite in this program cycle. It is
a user-local, agent-driven runbook using an isolated workspace/session, allow-listed provider/model,
explicit request/token/time budget, benign task, broad semantic checks, redaction, and cleanup.

Its output is dated scratch/research evidence. It supplies no operation-coverage or release-gate
credit unless a later release-policy decision promotes it.

## 8. Operation Inventory and Evidence Ledger

The generator emits a deterministic operation inventory containing at least:

- operation ID, group, accepted snapshot identity, and admission state;
- generated/internal/public ownership and member mapping;
- HTTP method and normalized path template;
- path/query/header/body channels and content type;
- success and distinct declared-error tuples;
- transport kind: JSON, no-content, SSE, text, binary, or WebSocket;
- stream metadata and exclusion fingerprints; and
- inventory schema version and digest.

The inventory subsumes the generation profile file; selected/pending counts become derived reporting.

A separate assurance ledger maps intentional scenario IDs to operation obligations, fixture kind,
OS/TFM scope, isolation class, and approved exemptions. The ledger is hand-authored and committed
under `tests/`; an `opencode-tool` verifier joins inventory, ledger, and emitted observations and
fails when an admitted operation has neither proof nor exemption. Runtime observations are emitted
separately and joined after execution; no closing test depends on parallel test ordering.

An admitted HTTP operation satisfies exact-pin success only through a typed real observed 2xx/204 or
a maintainer-approved, owned, refresh-reviewed exemption. Stub responses, raw reachability probes,
manual sandbox transcripts, canaries, and real-model runs do not satisfy this obligation.

## 9. Program Workstreams

The M-series remains the delivery frame: M4 carries workstream D plus the deterministic session
workflow; M5 carries workstreams B and C, opening with the minimal synchronizer and the first
accepted refresh; M6 carries workstream A's automation, workstream E's canary, and patch retirement.

### A. Snapshot and drift

- produce and review the current manual receipt;
- add minimum prepare/verify/apply tooling if the temporary SSE repair remains necessary;
- accept the refreshed snapshot only after current-tip ingestion and compatibility gates;
- add pinned-integrity checks, then scheduled detector/candidate/retirement automation.

### B. Surface compiler and runtime

- add typed per-call location;
- complete generic represented envelope payloads;
- admit the current-tip header and base64 shapes in the ingestion model, and the off-convention
  operation identities through reason-bearing curation rows;
- apply the sanctioned refresh and clean removed operations/stale curation;
- land query, naming, error, binary, and family batches;
- add exclusion fingerprints and normal PTY hand-written public ownership; and
- reach complete target admission before packaging freeze.

### C. Deterministic inventory and contract breadth

- emit the operation inventory;
- add the assurance ledger/verifier;
- generate complete deterministic success/error obligations for admitted operations; and
- retain deep risk-focused runtime tests in addition to breadth.

### D. Launcher and exact-pin process truth

- redesign M4 against current stdio/JSON readiness behavior;
- build the TUnit/CliWrap fixture and exact identity checks;
- implement/dogfood the launcher;
- add typed real-server success observations, platform cases, and dedicated global scenarios.

### E. Session workflow and canaries

- add the same-commit simulated-model session job;
- add the non-blocking latest canary and durable incident ownership;
- keep real-provider acceptance external until separately promoted.

## 10. Parallelism and Integration Checkpoints

After the design/canon decisions are landed, the first parallel wave may contain:

- manual snapshot candidate/receipt preparation;
- typed location plus normal PTY/token design in one runtime-ownership lane;
- envelope completion in a separate binder/emitter lane;
- operation inventory/assurance-ledger work;
- current M4 launcher planning and fixture design.

Required serialization:

- header ingestion and the public/runtime header decision share one owner;
- accepted refresh waits for current-tip identity/header/base64 ingestion;
- profile, curation, manifest, and generated-output integration has one owner;
- normal PTY WebSocket work waits for safe token minting and fingerprints;
- the Persistent PTY WebSocket door waits for the normal-PTY session machinery;
- blocking exact-pin ledger waits for a trustworthy server fixture;
- packaging waits for target admission, PublicApi/folder/handle/location/representation freeze review.

No two agents edit curation/profile/generated output concurrently. Independent source work may proceed
in parallel, but integration commits remain bounded vertical slices.

## 11. Proposed ADR and Canon Changes

The following are design outputs to draft and review before implementation. ADRs follow
`docs/adr/README.md`: a record exists only where a decision is hard to reverse, surprising without
context, and the product of a real trade-off; current mechanics live in architecture and engineering
canon. Existing records are revised in place (the `Date:` moves on a material revision); supersession
is reserved for wholly replaced decisions.

1. **New snapshot-production ADR:** the decision to produce the accepted document from an exact
   SHA with a normally empty patch list and temporary Restore patches under receipts, human apply,
   and mechanical retirement. Synchronizer mechanics live in
   `docs/architecture/protocol-and-generation.md`.
2. **New normal-PTY ownership ADR:** the hand-written public family/handles/session over the
   internal generated raw HTTP contract, with the split-ownership alternative recorded as rejected.
3. **New deterministic-evidence ADR (single paragraph):** deterministic exact-pin and
   simulated-model evidence gates releases; live-model evidence never does.
4. **Revise ADR-0003 in place:** refresh ownership relays to the synchronizer rather than
   copy-only mechanics.
5. **Revise ADR-0007 in place:** request options may carry typed location while stream
   error/options rules remain; a stream operation that first needs a meaningful per-call member
   receives a dedicated error-behavior-free stream options type.
6. **Revise ADR-0008 in place:** correct the empirical assumption that every upstream document
   carries complete SSE payload links, and relay to the normal-PTY ownership exception.
7. **Revise ADR-0013 in place:** point snapshot production at the new snapshot ADR; curation
   additionally owns reason-bearing operation-identity mapping rows for upstream-reported identity
   defects; wire types, constraints, formats, validation, and server-only runtime behavior remain
   outside it.
8. **ADR-0005 and ADR-0019 need no edit:** the v2-only scope stands, and ADR-0019 already judges
   placement at first admission and revisits it at a sanctioned refresh. Coverage-ledger and
   assurance-lane rules are conventions, not decisions: the target ledger, admission states,
   assurance planes, lanes, fixture, and ledger/verifier live in current engineering and
   architecture canon.

If approved for canon, one current assurance architecture document owns assurance planes, ledgers,
lane classifications, exact server/spec identity, and failure ownership. Testing authorship remains in
`docs/engineering/testing-style.md`; blocking commands/claims remain in `quality-gates.md`; exact
recipe procedure remains in `spec/SNAPSHOT.md`; rollout state remains in `docs/ROADMAP.md`.

The 2026-08-10 testing design remains dated rationale. Its valid three-link reasoning, isolation,
coverage-ledger, and canary material should be relocated rather than implemented verbatim; stale
dual-surface counts, owned fake-LLM design, stdout-regex launcher, replay promise, and full integration
cross-product are superseded by this design.

## 12. Completion Conditions

The program reaches its intended state when:

- the accepted snapshot is reproducible from its exact recipe and has no unexplained patch debt;
- scheduled observation reports current upstream drift without mutating accepted state;
- contract inventory and target ledgers are deterministic and reviewable;
- every target operation is callable or the target ledger is red;
- deterministic success/error obligations are complete for every admitted operation;
- exact-pin real-server success is observed for every required operation or a named exemption;
- normal PTY is usable end to end without caller-provided protocol magic;
- the same-commit simulated-model session job is blocking and hermetic;
- latest canary incidents have durable ownership;
- generated/PublicApi/TFM/OS/folder/handle/location/representation reviews are complete; and
- packaging policy is explicitly approved rather than bypassing `.generation-incomplete`.

## 13. Explicit Non-Goals

- Generate from floating upstream HEAD in ordinary builds.
- Automatically accept or publish an upstream candidate.
- Turn temporary patches into a standing local fork.
- Add server-only behavior to curation or normalized OpenAPI patches.
- Include SPA/static assets or generic CORS middleware in SDK operation coverage.
- Build the Persistent PTY WebSocket session door before the normal-PTY session machinery exists.
- Add arbitrary public request headers solely for location or ticket behavior.
- Make real-provider nondeterminism a blocking repository test.
- Implement the 2026-08-10 testing vision verbatim.
- Combine admission, deterministic contract, real-server, canary, and real-model evidence into one
  coverage percentage.
