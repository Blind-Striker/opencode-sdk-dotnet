# Runtime Pipeline Execution Plan — internal policy pipeline

Date: 2026-08-24

**Goal:** the hand-written Behavior core is rebuilt as an internal policy pipeline so that
each lifecycle rule has one owner, R16's pooled-body result lands inside a single body
owner, and M6's retry/telemetry/hooks have a named place to stand — without changing the
public surface, the generated-code-facing `Pipeline` entry points (except the sealed
adapter-contract change in Increment 2b), or ADR-0010's options-only construction.

Sealed inputs: research log Session 40 (Q126–Q129), the three 2026-08-24 architecture
scans and Azure/AWS peer evidence recorded there, ADR-0007/0009/0010/0014/0015,
`docs/architecture/client-runtime.md`, handoff HANDOFF-2026-08-24-13. The research gates
are satisfied: doc 17 (increment-gated allocation API survey), doc 19 (feature catalog with
the verified downlevel-codegen baseline), and doc 20 (runtime idiom audit with ranked
findings) are all landed 2026-08-24; adoption items below cite them rather than restate
them. None of the three reopens the sealed structure.

## Sealed design

### Composition (maintainer-sealed, Session 40)

Azure-style internal policy pipeline, ClientModel-aligned names, async-only, no mutation
API, no per-call policy splicing, everything `internal`:

```
PipelinePolicy[] policies = [
    new RequestDecorationPolicy(authorization, location, userAgent),   // today's Decorate
    new ResponseBufferingPolicy(pool),                                 // copy loop + progress timeout + pooled buffer
    new TransportPolicy(httpClient),                                   // send + FailureClassification + 3xx refusal
];
```

- `PipelinePolicy` — abstract class, `ValueTask ProcessAsync(PipelineMessage, ReadOnlyMemory<PipelinePolicy> remaining)`,
  slice-passing `ProcessNextAsync` (Azure `HttpPipelinePolicy` shape, single async path).
- `PipelineMessage` — `internal sealed`, `IDisposable`; members: `Request`,
  `Response { internal set }` (written by `TransportPolicy`), `CancellationToken`,
  `TimeSpan NetworkTimeout`, `BufferBody { init } = true` (stream plane sets `false`),
  `ResponseBody? Body { internal set }` (written by `ResponseBufferingPolicy`; `Dispose`
  returns a pooled buffer). No property bag; pipeline-written members are `internal set`
  (Azure discipline). Every new member names its writing and reading policy.
- The composed class keeps the name **`Pipeline`** and its generated-facing
  `ExecuteAsync`/`ExecuteStreamAsync` signatures, so Increments 1–2 produce zero generated
  diff. One-shot and stream planes are internal collaborators behind it.
- `ResponseMaterializer` — `internal sealed` instance class (holds `ResponseEncodingPolicy`),
  post-pipeline: decode + status-verdict consumption + adapter dispatch;
  `Materialize<TResponse>(message, adapter)` and `ReadErrorBody(message)` share one core.
  (AWS names this stage `Unmarshaller`; the repo keeps ADR-0014's "materialize".)
- Name table sealed: `PipelineMessage` / `PipelinePolicy` / `Pipeline` / `TransportPolicy` /
  `ResponseBufferingPolicy` / `ResponseMaterializer` / `FailureClassification` /
  `StatusVerdict` / `IEventStreamFramer` / `ServerSentEventFramer` / `FrameDispatch` /
  `UndeclaredStatusRule` message helpers fold into `StatusVerdictFailures`.

### Status authority (A3-Full, sealed)

The generated adapter is the **single authority** on what a status means under its
operation's pinned contract. Generator emits `StatusVerdict Classify(int status)` on
one-shot and stream adapters from the operation's status table
(`Success` / `NoContentSuccess` / `DeclaredError` / `UndeclaredError` /
`UndeclaredSuccess`); `SuccessStatusCode` and `ReadsSuccessBody` fold into it and are
deleted. Planes switch only on the verdict — no hand-written status-range logic remains.
The undeclared-success failure message has one author (`StatusVerdictFailures`).
Exception: **3xx stays in `TransportPolicy`** — a redirect is a protocol invariant no
operation can declare (the binder refuses 3xx), so it is transport's rule, not the
operation table's. Multi-success operations (200+204) later land as two switch arms with
zero new machinery. Noted behavior change: an unexpected 204 body is now drained into the
buffer and ignored rather than left unread (canon wording updated in Increment 2b).

### Timeout (sealed: progress semantics, Azure machinery)

- Semantics move from one total budget to a **progress timeout**: each read must make
  progress within `NetworkTimeout`; a slow-but-flowing body no longer dies. Default 100 s,
  internal-only for now; a public knob and an optional total-budget mode are M6 candidates
  (possibly surfaced through options/Extensions).
- Machinery: linked CTS over the caller token, `CancelAfter(NetworkTimeout)` re-armed per
  read inside `ResponseBufferingPolicy`'s own copy loop, dispose-to-interrupt for
  uncancellable reads, classification through `FailureClassification` (caller token
  inspected first). `Task.WaitAsync` abandonment and the `Stopwatch` budget arithmetic
  (`GetRemainingTimeout`) are deleted.
- Owned `HttpClient.Timeout` becomes `Timeout.InfiniteTimeSpan` (the pipeline owns
  timeouts; two mechanisms must not race).
- A live SSE success body (BufferBody=false **and** verdict Success) is exempt — it stays
  live until caller cancellation, server completion, or failure (existing canon line).
  `ResponseBufferingPolicy` buffers every other case, so the R17 stream-open error-body
  protection survives under progress semantics.

### Body and encoding (sealed)

- R16's mechanism is accepted, its dirty-worktree code is not: the ArrayPool-backed
  growable destination with ownership separated from the pending copy is **re-derived
  inside `ResponseBufferingPolicy`** on the new shape. The cancellation-vs-fault race is
  designed out via `FailureClassification`'s caller-token precedence; the `CanWrite` and
  facade findings die with the old shape. `TrackingByteArrayPool` and the R16 test intent
  carry over. Buffer lifetime is owned by `PipelineMessage.Dispose` — no Dispose
  obligation reaches the planes. No per-operation buffering-strategy selector (one
  strategy, one owner; a selector with one policy is a hypothetical seam).
- `ResponseEncodingPolicy` keeps **full `HttpContent.ReadAsStringAsync` parity on both
  planes** (ADR-0014's sentence stands unchanged). It becomes a genuinely
  high-performance, low-allocation internal feature: no exception-based control flow
  (`Utf8.IsValid` on modern TFMs, scan fallback under `#if` on downlevel), no substring
  allocation for quoted charsets, span-based probes. Acceptance: a **differential parity
  test matrix** against real `HttpContent` decoding (quoted charset on both planes,
  charset+BOM combinations, UTF-32/16 BOM precedence, empty-body-before-invalid-charset,
  the net472 leg) — closing review gaps R09/R10 — plus benchmark evidence.
- The pre-parse validity scan is irreducible under parity (replacement decoding can turn
  invalid bytes inside a string literal into *valid* JSON, so path selection must precede
  parsing); it stays, SIMD-cheap.

### Failure classification (A5, sealed)

`FailureClassification.Map(exception, token, phase)` is the one owner of the
caller-cancel-versus-transport rule (both peers use the same order: caller token first).
The four catch cascades in `Pipeline`/`ResponseBodyReader` become single calls; phase
supplies the message; M6's "is this retryable?" question gets its home. BCL-derived
knowledge — upstream schema churn cannot touch it.

### Stream plane (sealed)

`FrameDispatch` (failure-name throws, default-name yields, other names refuse, the two
deserialize helpers) moves beside `IStreamAdapter` and is tested with `ServerSentEvent`
values — no HTTP, no reader. Framing arrives through the named seam `IEventStreamFramer`
(`IAsyncEnumerable<ServerSentEvent> ReadAsync(Stream, CancellationToken)`);
`ServerSentEventFramer` is a stateless facade constructing one `ServerSentEventReader`
per body (the reader stays one-per-body stateful). Plane sequencing tests may substitute
a scripted framer; principle recorded: **a seam gets a name (interface/abstract class),
never a delegate parameter**. Stream retry is out of scope by canon (no auto-reconnect;
a live stream is not replayable) — recorded as an M6 constraint.

### Knowledge-source provenance (sealed principle)

Every centralized policy module states its knowledge source in its doc comment:
`pin-derived` (adapts through regeneration; fail-closed on drift), `BCL-derived`
(platform behavior), or `upstream-observed` (the fragile list — e.g. the location-header
percent-decoding asymmetry — re-verified at every spec refresh).

### TFM rule (sealed)

Algorithm divergence → per-TFM adapter behind the owning seam; API-shape divergence →
`#if` in place. A2's divergence is API-shape (stays `#if`); transport policy divergence
(A6, deferred) is algorithmic (per-TFM adapters when it lands).

## Increments

Each lands independently green through the full quality gate; perf-relevant ones carry
same-environment before/after benchmark evidence. Increments 1–2 are behavior-preserving
with zero generated diff.

### Increment 0 — archive the R16 experiment

- [ ] Save the complete dirty diff plus untracked files as a patch under
      `C:\bench-artifacts\r16-experiment-2026-08-24.patch` (operational evidence,
      never referenced from product files), then restore a clean worktree.

### Increment 1 — `FailureClassification` (A5)

- [ ] Extract the map; the four cascades become single calls; existing
      `InnerException`-type assertions become data-driven table tests plus wiring checks.

### Increment 2 — policy-pipeline skeleton (behavior-preserving relocation)

- [ ] `PipelineMessage`, `PipelinePolicy`, the three-policy roster, planes,
      `ResponseMaterializer` (absorbing the three duplicated read sites — closes R12),
      `IEventStreamFramer` + `ServerSentEventFramer`, `FrameDispatch` beside
      `IStreamAdapter`. Total-budget semantics retained in this increment
      (`WaitAsync` machinery moves, not yet replaced). Benchmarks compare against the
      Increment 1 baseline.
- [ ] Audit fold-ins (doc 20 D2/D3, behavior-preserving): the location-header
      `Uri.EscapeDataString` moves to construction inside `RequestDecorationPolicy`
      (the `_authorization` precedent), and the JSON `MediaTypeHeaderValue` becomes one
      shared static with a mutability-discipline comment. Internal policy hops are
      `ValueTask`-shaped per doc 17 §5 / doc 20 A3; public surfaces stay `Task`.

### Increment 2b — A3-Full status verdicts (generator increment)

- [ ] `StatusVerdict` + generated `Classify` on one-shot and stream adapters;
      `SuccessStatusCode`/`ReadsSuccessBody` deleted; planes consume verdicts only.
      Generator gates (`generate --verify`, snapshots, PublicApi unchanged). Canon
      wording touch: unexpected no-content bodies are drained and ignored.
- [ ] While the error-reader neighborhood is regenerated: the comparer-overload
      `Enumerable.Contains` on the typed-error path becomes a `foreach` over the tag
      array (doc 20 E5).

### Increment 3 — pooled buffering + progress timeout (behavior change, red-first)

Entry checkpoint (decision session before source work starts): the accumulated dependency
decisions land as one batch under the explicit-vs-transitive rule — `Microsoft.Bcl.Memory`
(downlevel `Utf8.IsValid`, doc 17), `Microsoft.Bcl.TimeProvider` versus an internal clock
seam (doc 19 #8), `System.Collections.Immutable` (downlevel Frozen collections, doc 19 #5),
and `System.Net.ServerSentEvents` (stage-2, may be deferred to that design). The rule
itself — declare explicitly what our source uses directly, what appears on public surface,
or what we version-pin for behavior; trust transitive otherwise — seals into
`docs/architecture/platform-and-packaging.md` with per-edit maintainer approval, and the
already-due consequences apply here: explicit downlevel `System.Memory` and
`System.Buffers` references land with this increment's direct `ArrayPool` use.

- [ ] R16 mechanism re-derived inside `ResponseBufferingPolicy`; linked-CTS
      `CancelAfter` + dispose-to-interrupt copy loop; owned client `Timeout` → infinite;
      SSE exemption and error-body buffering rules as sealed above.
- [ ] Adoption details from the research (doc 17 §2/§4, doc 19 #9/#10): the
      operation-scoped dispose registration uses `CancellationToken.UnsafeRegister`
      (Polyfill maps it to `Register` downlevel), escaping copies use
      `GC.AllocateUninitializedArray` under `#if NET`, and the pooled buffer's
      `clearArray` security-versus-cost choice is decided explicitly (upstream does not
      clear; clearing is deliberate hardening).
- [ ] Canon edit in the same change: `client-runtime.md` timeout paragraph rewritten to
      progress semantics; old total-budget tests replaced red-first (stalled body dies at
      the window; trickling body survives past it).
- [ ] Same-machine allocation evidence; target: reproduce R16's fixed-cost rows
      (net10 ≈ 1.7 KB pipeline row at every body size).
- **Gate satisfied:** doc 17 §2 (contiguous ArrayPool grow-and-copy, per the net10
  `LimitArrayPoolWriteStream` blueprint; `IBufferWriter`/segmented designs ruled out) and
  doc 20 A1/A2.

### Increment 4 — encoding hardening

- [ ] Parity-preserving low-allocation rebuild of `ResponseEncodingPolicy`; differential
      parity matrix (R09/R10 closure) and benchmark rows.
- [ ] Adoption details from the research: `Utf8.IsValid` replaces the
      `DecoderFallbackException` control flow (doc 20 C1; downlevel per the checkpoint's
      `Microsoft.Bcl.Memory` decision, else the strict-decoder try/catch stays behind the
      per-TFM adapter); BOM tables become `u8` span data (doc 19 §0 — zero-alloc on all
      five TFMs); the well-known-charset fast path uses span constant-string patterns
      with `Ascii.EqualsIgnoreCase` as the `#if NET8_0_OR_GREATER` variant (doc 19
      #2/#3); the double `GetPreamble()` call collapses to one local (doc 20 C3);
      downlevel keeps `(byte[], int, count)` overloads — Polyfill's span-Encoding shims
      allocate and are excluded from hot paths (doc 17 §1).
- **Gate satisfied:** doc 17 §1/§5 and doc 20 C1–C3.

## Research status / open

- All three research legs are landed (docs 17/19/20); the increment gates above are
  satisfied. Items the research surfaced but no increment schedules — reserve tools
  (`OverloadResolutionPriority`, `params ReadOnlySpan` helper overloads,
  `PoolingAsyncValueTaskMethodBuilder`, `HttpHeaders.NonValidated`) — stay in doc 19's
  ranked table, adopted only with benchmark evidence.
- SSE stage-2 remains a separate future design (not implied by Increment 2's framer
  seam). Its research inputs are ready: the `SseParser` blueprint and strictness decision
  (doc 17 §3), `Utf8Parser` for `retry:` digits and `u8` field names (doc 19 §4/§1), and
  the profiling-gated span-keyed event-name cache (doc 19 #11, doc 20 B2).

## Deferred, with triggers

- **A6 configuration/transport split** — deferred. Trigger (ROADMAP): when M6 attaches
  telemetry/hook handlers to the transport, or when Extensions gains a concrete
  `IHttpClientFactory`/named-client need, A6 lands first. Also reopen if the
  validate-after-build ordering hazard bites.
- **Total-budget timeout option + public `NetworkTimeout` knob** — M6, possibly public
  through options/Extensions.
- **Generator typed-switch union dispatch, widened into the emitter allocation batch** —
  unchanged trigger (after the runtime increments, before or with M4 planning, per
  ROADMAP), but the batch now also owns the audit's emitter-side findings so the
  generated dispatch neighborhood is rebuilt once: the per-payload union tag string
  (doc 20 B2 — net9 `GetAlternateLookup` + `CopyString`), `FrozenDictionary` dispatch
  tables (doc 20 F1, pending the Increment 3 checkpoint's `System.Collections.Immutable`
  decision), the tag→`JsonTypeInfo` double hop (F2), cached empty-request instances
  (D5), and the benchmark-gated route/query composition items (D1, ValueStringBuilder
  pattern — doc 19 #6). Doc 20's ranked table is the batch's backlog; nothing from it is
  scheduled earlier.
- **B-track (generator tool)** — outside this plan. B1 facet binders and B2
  reserved-name policy are recorded in ROADMAP as an untriggered locality item, priority
  decided separately.
- **M4 launcher plan** — starts after the increments above; unchanged scope (ADR-0001).

## Test migration map

| Today | Destination |
|---|---|
| `PipelineTests` timeout/classification sections | `FailureClassification` table tests + Increment 3 progress tests |
| `PipelineResponseOwnershipTests` (4 bespoke `HttpContent` doubles) | `ResponseBufferingPolicy` tests |
| `ResponseEncodingPolicyTests` | differential parity matrix (Increment 4) |
| R16's `TrackingByteArrayPool`, `ResponseBodyReaderTests`, `PooledResponseBodyStreamTests` intent | `ResponseBufferingPolicy` tests (re-derived) |
| `Pipeline` stream dispatch tests | `FrameDispatch` value tests (no HTTP) + scripted-framer plane tests |
| Generated adapter status tests | `Classify` verdict tests (Increment 2b) |

## Canon edits (applied inside their increments, never ahead of them)

1. Increment 2b: no-content wording ("drained and ignored") in `client-runtime.md`/ADR-0007
   relay text.
2. Increment 3: `client-runtime.md` timeout paragraph → progress semantics.
3. No other canon changes: ADR-0014's decoding sentence stands; ADR-0010 untouched; the
   internal-policy-pipeline decision itself is an ADR candidate to seal alongside
   Increment 2 (maintainer decides at that commit).
