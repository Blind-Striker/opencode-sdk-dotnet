# Roadmap

Date: 2026-08-30

Operational state: what is done, what is next, what is open. This file shrinks as work lands.
`../AGENTS.md` routes to current architecture and engineering canon; decision records live in
`adr/`.

## Status

**M1 is complete.** The SDK targets the v2 protocol surface only (ADR-0005; `spec/` pins
v2-branch commit `a6a712a`), and the walking skeleton runs end to end: SpecIR plus curation
bind into client/operation/envelope/route/error plans, generic emitters render the callable
surface as committed source under `src/OpenCode.Sdk` (`OpenCodeClient.GetHealthAsync`,
`Sessions.GetSessionClient(...)`, `SessionClient.GetMessageAsync`, guarded envelopes,
`OpenCodeRoutes`, response adapters), and one hand-written `Pipeline` owns endpoint authority,
Basic-auth/User-Agent decoration, buffering, throw-versus-`NoThrow`, and transport-failure
mapping. The public API is locked in a reviewed `PublicApiGenerator` baseline; the writer
refuses unmanifested overwrites and headerless manifest entries; packing still fails on the
partial-operation marker while breadth is pending. Demonstrated live 2026-08-13 against
`opencode2 serve` v0.0.0-next-17403 (pin `a6a712a` — deliberate version skew accepted): both
generated methods returned typed 200 payloads (`ServiceHealth`; `SessionMessageAssistant`
with its wire `id`).

A verified multi-agent review of PR #16 (2026-08-13) produced a milestone-anchored queue:
the blocker set (typed-spine leaks, silent route rewriting) lands on the PR branch itself
(#17) together with the performance-test infrastructure (#18); every other finding lives in
issues #19–#25 pinned to the milestone that resolves it — nothing on that list outlives the
M series.

**M2's first breadth batch is complete** (decisions: research log Sessions 19–20).
`session.list`, `session.get`, `session.create`, and `message.list` are callable
through their final generated surface — uniform `*Request` operation inputs (Q83) with the
query records riding the `ListRequest` seam, `SessionCreateRequest` bodies through the
pipeline's JSON path, cursor-list envelopes with the shared `ListCursor`, query-composing
routes, the `Session.Info` model closure, and the first 5xx arm. Riders #19 (carrier converters), #21 (fail-closed walls + P2 single-pass
envelopes; the interim baselines that guarded the M3 arcs have since retired with the M3 plan in
favor of the permanent suite),
and #22 (List/Create verb rules + C17–C20) landed with it; upstream's `InvalidRequestError1`
duplicate collapses through the new `schemaAliases` curation. Demonstrated live 2026-08-14
against `opencode2 serve` v0.0.0-next-17403 (create → list → get → messages, wire cursor
round-tripped). The #20 decision landed (blank explicit passwords refuse; the environment
fallback was later removed by Q90 — `null` sends anonymous requests) and #25 closed keep. The alignment batch is complete — the uniform
`*Request` rename (Q83), the feature-slice layout migration (Q84), and the Extensions
bring-up (Q85) — and the follow-on construction/options/DI work (research log Q90/Q91)
landed on the same PR. That intermediate factory/public-transport shape was later superseded by
Q92; ADR-0010 and the M3 status below own the current construction. The PR #26 external review ran
through adversarial verification (36 findings: 30 confirmed / 4 plausible / 2 refuted), and the
verified fix batch landed 2026-08-15 red-test-first — all ten merge blockers plus the small
confirmed fold-ins. A second full-diff review (15 verified
findings) closed the same day: six runtime/emitter fixes landed on the branch (timeouts
route onto the transport spine, conflicting BaseAddress refuses, handle guards match the
route guards, carrier payload guards, count-guard identity, the Pagination shadow wall)
for 932 green tests, and every surviving finding lives milestone-anchored with an explicit
trigger — #31 (M2, DI lifetime shape, decision-first), #32 (M3, EscapeDataString TFM
policy), #33 (M3, carrier hand-construction semantics, rides #23), plus the #27 and #23
enrichments. PR #26 is merged. The #34 contract-test consolidation landed as its own
follow-up PR: one shared contract scaffold (`ContractScenario`, `WireBodyData`), a single
`FixtureLoader`/`RecordingHttpHandler` pair compiled from `tests/Shared` into both the SDK
and Extensions test projects, and the previously unobserved object-envelope
`{"data":null}` refusal pinned by test (the support pair under `tests/Shared` is consumed
by the SDK test project; the Extensions tests are registration-topology only).

**M3 is complete** (plan retired 2026-08-27; decisions: research log
Sessions 24–37). Q92's simplicity-first construction (ADR-0010) is landed — Arc 1 complete:
the transport constructor is internal friend-assembly test surface, Extensions registers
a factory-less singleton client family with a roster contract test, and the Q91 guard
machinery is deleted — closing #31 by construction. #32 and #33 completed in Arcs 5 and 6,
and the location + merged-Request design is
sealed (Session 25, Q93/Q94: the binder placement map and the dual-channel location
rendering). **Arc 3a's SSE engine is landed** — `ServerSentEventReader` frames a live body
into named events, and the pipeline opens a stream through the same decoration and status
walls the one-shot path uses. Q98 sealed what the `event` field is for
(the contract's only mid-stream failure channel; upstream's own generated client discards
it and we do not), and Q99 sealed that a body cut mid-event is reported rather than
dispatched. **The generator side landed too**: ADR-0011 turned union membership into an
interface because 39 schemas branch from both stream unions, a marker-spanning nested union
now dispatches through its own leaves, the two unions carrying no choice are read rather than
refused, numeric literal markers bind, and a streaming success binds to a stream plan carrying
its frame, JSON-encoded payload, and declared `failureEvent` metadata.
`v2.session.log` is selected and `SessionClient.GetLogAsync` returns
`IAsyncEnumerable<ISessionLogItem>` with no per-call options. Demonstrated live 2026-08-17
against `opencode2 serve` v0.0.0-next-17403: the stream opens, frames decode, and the
watermark types as `EventLogSynced`; research doc 02 records why the default server advances
the watermark without persisting historical payload rows.

The post-Arc 3a independent review and factual verification are complete (research log Session
28). Fifteen trigger-scoped findings live in #39–#53; #23/#24/#27/#30 carry same-owner riders.
Stream lifecycle, cancellation parity, and strict SSE UTF-8 are closed (#39/#42 SSE arm).
Special-number schemas retain their provenance through binding and emission (#41): ordinary
numbers and exactly `"NaN"`, `"Infinity"`, and `"-Infinity"` round-trip through source-generated
metadata and arbitrary numeric strings remain unrepresentable as ordinary doubles.

Session 29 reset two cross-cutting boundaries before more model breadth lands. The pinned OpenAPI
document is now the sole protocol-semantic generation input; curation cannot restore types,
constraints, formats, or validation from upstream implementation source (ADR-0013). #54 removes the
former property-type and mutually-exclusive-query curation sections, name-derived positive counts,
and string-enum boolean conversion. Selected `limit` and `after` queries retain the pinned
document's string shape; `follow` preserves its exact string tokens through `QueryBoolean`, and
exact `asc`/`desc` order remains typed from its declared enum. Runtime now
validates only transport/framing, .NET materialization, and union dispatch; it does not replay
server schema validation, normalize optional collections to empty, or defensively own generated
model collections (ADR-0014). This supersedes the old acceptance criteria behind #45/#48 and the
source-derived parts of #28/#40 plus the explicit-null arm of #41. #54 is complete at `c0003d1`
with three-OS hosted run `32069792901` green. #55 now emits the required/nullable axes directly,
keeps optional collections nullable, exposes shallow init-only collection references, delegates
collection-child nullability to static annotations, and ignores bodies on declared no-content
statuses. The synthetic matrix binds, emits, source-generates, compiles, and round-trips scalar,
list, dictionary, value-type, unrestricted, literal, and union shapes without reflection fallback.

Q109 seals a pre-#46 representation correction: a serializer-proven in-band JSON-null carrier stays
non-nullable at required properties and present collection slots because its canonical CLR state
already materializes wire null. `JsonElement` is the current carrier through
`JsonValueKind.Null`; optional outer properties remain nullable for absence. The implementation is
locally complete as its own reviewed increment because the binder change is small but intentionally
updates 77 unrestricted dictionary-value signatures in the generated PublicApi. All six local gates
are green with 1,229 tests; the source increment is committed at `075e000`, and three-OS hosted
run `32115094777` is green at handoff tip `7abab38`.

#46 then made known-object unknown-field skipping explicit in generated JSON metadata and added the
fail-closed named-object + typed-additional-properties binding wall at `ca79254`; three-OS hosted
run `32123321173` is green. The current exact aliases carried by #27 landed at `7123627`:
`Session.Inbox.SyntheticPayload1` and `Session.Inbox.UserPayload1` now collapse through structurally
validated curation onto their canonical payload schemas, with generator-owned model, registry,
manifest, owner, and PublicApi changes. Three-OS hosted run `32128587050` is green, and the local
suite now executes 1,240 tests with none failed or skipped. #27 remains open for its later
first-occurrence and M5 walls.

The #55 allocation-first comparison against untouched `050b4f8`, under .NET SDK `10.0.302`, .NET
`10.0.10`, and concurrent workstation GC, measured `GetMessageAsync` at 26,548 → 22,606 B/op
(`0.852x`), `ListMessagesAsync` at 27,316 → 23,316 B/op (`0.854x`), and deep-union deserialization
at 17,786 → 13,842 B/op (`0.778x`); `GetHealthAsync` stayed 2,128 → 2,128 B/op. Corresponding mean
ratios were `0.888x`, `0.837x`, `0.898x`, and `1.012x`. #47 closes the stream-plan contract
before live-bus breadth: frame `id` and `event`, SSE encoding, failure-event separation, and the
no-body runtime boundary fail closed. Stream methods document their always-throw error channel
without exposing request options. A generic stream profile binds, emits, source-generates, and
compiles while pinning its route, payload/failure metadata, and complete error map; the selected
`session.log` contract pins GET plus 400/401/404.
The fresh-context review reported no findings after its one low-severity test-helper duplication was
resolved; all local gates are green with 1,255 tests and none failed or skipped. Hosted run
`32178776110` is green on Linux, Windows, and macOS at `2f48d11`, and #47 is closed.

The bounded CI-gate pass establishes build as the semantic analyzer wall and splits the former full
format pass into physical whitespace and warning-level Roslyn style gates. Controlled probes proved
build catches IDE0055, build-enforceable IDE style, SDK CA, and third-party diagnostics; the style
pass retains build-inert simplification rules and deterministic import organization without a
maintained diagnostic allow-list or a second solution-wide third-party analyzer pass. Generator
output keeps its narrower project-scoped, generated-path-only mutating full formatter. Warm local
solution linting fell from 117.00 to 67.29 seconds while preserving the existing style policy. A
separate analyzer-cost probe measured 42.96 seconds with analyzers and 10.72 without, but no target
or OS analyzer coverage is weakened without a dedicated coverage-preserving design. Hosted run
`32188680204` is green on Linux, Windows, and macOS at `005030f`: Linux whitespace took 17 seconds
and style 1:34, totaling 1:51 versus the previous 3:00 full solution lint pass. Generator verify
retained its project-scoped full formatter and took 1:16. Linux build variance rose to 4:26 and
Windows completed in 10:04, so the matrix wall remains build rather than formatting.

#49's pre-Arc-3b evidence is complete at `8d5d537`: the four central message fixtures use valid
`msg_` identifiers, every bound converter tag maps mechanically to its declared variant, and every
converter-required variant/interface/unknown-carrier type is proven through `RegistryPlan`, the
single emitted source-generated registry, and fresh compilation. Runtime evidence remains honest:
two of 40 durable branches, the `log.synced` watermark, and unknown-carrier behavior. Hosted run
`32192213328` is green on Linux, Windows, and macOS. Arc 3b now completes the deferred plural-interface
closure without inventing a 40/87-payload corpus. The six interim allocation
baselines served as comparison guards until the permanent per-operation suite and the
`.benchmarks/` store replaced them; master protection and direct-push policy remain open under
#50. #53's typed stream failure-cause M3 subset is complete at `28d09e1`: the pinned cause contract
survives ingestion, binding, model/union planning, registry emission, source generation, and
compilation; `not: {}` remains never, its uninhabitable `Fail` branch is a known protocol refusal,
and valid `Die`/`Interrupt` or genuinely unknown causes ride `OpenCodeStreamFailureException` as
typed data. All 1,290 local test executions are green, and hosted run `32233455578` passed on Linux,
Windows, and macOS. #53 remains open for its deferred M5 wall inventory. **Arc 3a deliverable
closure is complete at `c7a35bd`**, with hosted run `32240794296` green on Linux, Windows, and
macOS. The committed sandbox now has a Generic Host worker that obtains a bound `SessionClient`,
consumes `GetLogAsync` with the host stopping token, and was repeated live against
pinned-compatible `opencode2` `0.0.0-next-17403`: generated
`EventLogSynced` materialized, then SIGINT drove normal host shutdown. The new end-to-end
pipeline/reader/generated-adapter benchmark measured 64 large frames at 1.157 ms and 717.82 KB per
complete response, and 1,024 small frames at 1.674 ms and 507.20 KB on the recorded Linux/.NET 10
environment. Research Q113/Q114 own the exact setup, statistics, and environmental limits.

**Arc 3b's live global-bus breadth is complete at `8d2a79d`**, with hosted run `32269060251`
green on Linux, Windows, and macOS. `v2.event.subscribe` emits as the
parameterless `Events.SubscribeAsync(CancellationToken)` through reason-bearing operation and schema
name curation; the profile is now 15 selected / 105 pending. The pinned live-event closure passes the
same converter, registry, source-generation, compilation, and reflection-disabled runtime evidence
as the durable log. Shared session leaves implement both `IEvent` and `ISessionEventDurable`, and the
marker-kind wall refuses contradictory plural membership. The first selected heterogeneous
structural unions use ADR-0016's generated token-dispatched carriers; same-primitive refinements emit
no dead models. Representative runtime evidence remains one shared known leaf plus unknown/failure
paths, while converter maps prove structural breadth mechanically. Demonstrated live on Linux against
`opencode2` `0.0.0-next-17403`: generated `EventServerConnected` and `SessionCreated` frames
materialized, SIGTERM drove normal host cancellation, and the separately launched server remained
healthy until stopped independently. Research Q115/Q116 own the decision, commands, identities, and
limitations. #44 and #49 are closed; no later M3 arc or #53 M5 wall work began in this increment.

**Arc 4's paginator is complete at `ec043f4`**, with hosted run `32338694450` green on Linux,
Windows, and macOS. ADR-0017 keeps generated `ListMessagesAsync` as the explicit page/cursor/
`NoThrow` door and adds `EnumerateMessagesAsync` as a lazy item sequence over the same virtual method
and response adapter. The binder admits the companion only for the exact `ListRequest` plus cursor-
list dialect; the hand-written core follows opaque `cursor.next`, while generated metadata retains
the string `limit`, omits first-page-only `order` on continuations, and never decodes or compares
cursors. Deterministic evidence covers an unchanged initial order+cursor request, empty intermediate
pages, null-only termination, typed later-page errors, cancellation between buffered items, an empty
cursor, and the mocking seam. The PublicApi review contains one additive method. Research Q117/Q118
own the survey, decision, implementation, local 1,338-test closure, hosted matrix, and a Linux live
run where the committed sandbox enumerated two real historical messages with `limit=1` across two
pages without creating data or invoking a provider.

**Arc 5's owned-transport/net472 GA cluster is complete at `b261014`**, with hosted run
`32350952168` green on Linux, Windows, and macOS. Every owned handler refuses automatic redirects and
the pipeline classifies surfaced 3xx as protocol failures before body reads on both one-shot and SSE
paths. Downlevel endpoint-and-proxy `ServicePoint` settings lift connection starvation and reapply the
modern 120-second rotation policy before each owned send. Real-handler evidence covers redirect
surfacing, modern policy, proxy selection, and two same-authority net472 streams plus an ordinary
request; separate trackers prove response/content disposal on every one-shot exit. #32's generated
path/query boundary now refuses lone surrogates and values over 32,766 UTF-16 code units uniformly
without weakening empty-cursor or path guards. PublicApi and manifest membership are unchanged;
research Q119 owns the review corrections and local 1,374-test closure. #43 and #32 are closed.
**Arc 6's measured performance pass is complete at `fa6124d`**, with hosted run
`32374393085` green on Linux, Windows, and macOS. Known tagged unions now dispatch through a copied
reader and materialize once; only unknown carriers retain a DOM. Valid UTF-8 successes avoid the
discarded UTF-16 body while error `RawBody`, charset/BOM/replacement behavior, `NoThrow`, caller
cancellation, timeout budgeting, and response ownership remain pinned. The generated collection
comparison retained shallow `IReadOnly*`; Native AOT and downlevel compile probes passed without a
public shape change. The downlevel SSE append path replaces Polyfill's whole-line string with a
dedicated reusable buffer, with the Windows net472 leg exercising its long-line and timeout tests.
PublicApi and manifest membership are unchanged; research Q120 owns the measurements and review
corrections. #23, #29, and #33 are closed. The post-Arc 6 benchmark-only follow-up decomposed the
permanent performance suite into per-operation component ladders with exact-byte and wire-size
columns and medium/large fixtures; research Q121 owns its same-environment measurements. The
independent Arc 6 review (research Q122) confirmed a streaming-deserialization regression in the copied-
reader union scan, an unbounded stream-open error-body read, a raw `NotSupportedException` leak for
`charset=utf-7`, a downlevel SSE cancellation gap, and five test gaps. **M3 is functionally complete;
the bounded Arc 6 repairs are complete at `3f68ddf` and `713f09a`, including real-handler net472
cancellation/timeout evidence. The first net472/net10 benchmark leg is complete (research Q123), and
R18 stage 1 now scans decoded spans by line with 3.1-8.9x sustained parser speedups and no allocation
regression (research Q124). R16's dirty-worktree experiment proved that a pooled read can remove the
second body-sized allocation, but review exposed another lifecycle race and the growing Pipeline now
needs a holistic design decision (research Q125). The runtime-pipeline design session is complete
(research Q126–Q129): an internal policy pipeline is sealed with a three-policy day-one roster,
generated status verdicts on the adapters, a named framer seam, progress-timeout semantics, and R16's
mechanism re-derived inside the buffering policy. The modern-allocation research legs are complete
(docs 17/19/20: API survey, feature catalog with verified downlevel codegen, runtime idiom
audit).**

**The runtime-pipeline arc is complete.** Its staged plan was fully executed through Increment 4
(evidence: research Q130–Q138; composition: ADR-0018) and is retired together with the M3 plan
(maintainer, 2026-08-27 — consumed plans delete when their work ships). Landed: the
`FailureClassification` phase map, the behavior-preserving internal policy pipeline, generated
`StatusVerdict Classify` verdicts on the adapters, pooled buffering with the progress timeout, and
exception-free encoding over `Microsoft.Bcl.Memory`; Increment 0 archived the R16 experiment out of
tree. The Increment 3 entry checkpoint (Q133) sealed the downlevel package batch:
`System.Memory`/`System.Buffers` adopted, the `TimeProvider`/clock seam declined with an M6
trigger, `System.Collections.Immutable` deferred to the emitter allocation batch, and
`System.Net.ServerSentEvents` deferred to SSE stage-2. Acceptance ran green 2026-08-25 against the
real v2 server built from the pinned commit (Q136): one-shot, stream (typed `EventLogSynced` plus a
live SSE hold), and events (live `SessionCreated` dispatch); pagination enumerated an empty session,
so a non-empty pass waits for a provider-configured session. The arc-milestone benchmark (Q138)
closed the arc: body-size-proportional allocation is gone from the pipeline (the net10.0 one-shot
row is flat at 2,112 B at every size; complete large calls drop ~2.15 MB), downlevel calls run
roughly twice as fast, and every added cost is fixed, small, and named. GitHub Actions
execution is restored (billing refilled 2026-08-25): the CI-hardening commit `9da0ae3` passed
all three OS jobs on its rerun and the subsequent pushes run normally; #50 stays open for
branch protection and required checks. The
**spec refresh is blocked upstream**: the 2026-08-25 attempt found the current upstream document
has lost its SSE payload schemas to the effect beta.107 regen, so the pin stayed at `a6a712a3` —
research Q139 owns the evidence (superseding Q137's stream-channel claim), and the regression is
reported with a verified restore path as
[anomalyco/opencode#44911](https://github.com/anomalyco/opencode/issues/44911). ADR-0020 has
since converted that wait into a Restore-patch path (see the program paragraph below). The queue was
reordered at the M3 boundary (maintainer, 2026-08-25) for the now-public repository: first the
**emitter allocation batch**, now executed on the `a6a712a3` base (research Q140–Q143). The
records infrastructure is in place — the git-ignored `.benchmarks/` store seeded with the local
artifact history, driven by `opencode-tool compare-benchmarks` (exact allocation columns plus an
indicative median ratio, replacing the session-scratch extract script). The batch landed the
shared empty-request instance, the frozen tag tables with `System.Collections.Immutable`
conditioned to the downlevel targets (maintainer-sealed), and the net9.0+ alternate span lookup
that stops materializing known union tags (net10.0 union rows lose exactly their tag strings;
downlevel keeps the string path — Polyfill's alternate lookup is an O(n) scan). Route/query
composition measured as an honest negative behind its own permanent rung. Doc 20's D2/D3 had
already landed inside the runtime arc. The **benchmark coverage batch**'s code half is landed
(2026-08-29): the `compare-benchmarks` CSV completeness fix (B1, `98e682a`) gives one-sided
before-only/after-only cases exact allocation figures and a `Status` column instead of vanishing
from the durable artifact, and three new rungs (B2, `b672120`/`aff51d4`/`1d133c5`) landed — a
`PtySession` read-path ladder, a location-merge case on the route-composition rung, and a
dictionary-envelope ladder over the generated `SessionActiveResponse` adapter, with reasoned skip
notes for the Data-list and bare-container shapes already covered by the MessageList/Health
ladders. **The closing default-job comparison against `arc-milestone-default` landed**
(`.benchmarks/post-sweep-default`, `post-sweep-comparison.csv`; findings in the 2026-08-29
handoff): .NET 10.0 allocation −1,847,984 B (48 improved / 21 same / 7 worse by ≤64 B); net472
+56,615 B (47 regressed, `MessageList` 50,242 B of it) — unattributed, a bisect across the spec
refresh and the envelope arc is queued; allocation amplification is scale-invariant at ~5.8×
and belongs entirely to deserialization (the transport allocates a flat 2,128 B across a 490×
payload range); the fixed per-call buffers are named (`PtySession.ReadFramesAsync` 16 KiB per
read, the SSE reader ~26 KB per open, `SessionActive.GetActiveAsync` 53×). The frozen-table
timing verdict is that there is none: identical code four hours apart moved 87 of 96 cases by
more than 10% on time while allocation was byte-identical on all 96, so timing on this
workstation is never evidence — allocation is the axis.
**M5 breadth batches are pulled ahead of M4 and are landing.** The Q144 wall-probe mapped the
full pinned surface (99 workable pending operations after Q137/Q139's drift map excludes the
upstream-removed families — the question flow, `projectCopy.*`, `health.stop`,
`project.directories`; 52 are routine curation-only admissions, and the 47 refusals partition
across the bodyless-POST, inline-promotion, envelope-shape, PUT, and query walls plus three
singleton decisions). The doc 18 gate is decided on that probe (maintainer, 2026-08-25): B2's
single `ReservedNamePolicy` is landed with its reflection coverage tests, B1's facet binders
are landed ahead of the first mechanism batch, and B3/B4 stay sequenced behind B1. **The A-series is
complete**: all 52 routine operations landed in four family batches with contract tests,
Extensions roster growth, and additive-only PublicApi reviews, taking the profile to 67
selected / 53 pending across twenty-one client families. ADR-0019 owns handle placement
(working objects keep handles; single-action families take the id as an argument), and every
group curation row now carries a mandatory reason. The committed sandbox's session-actions
walkthrough ran live against the pinned server: fork's request-side union accepted on the
wire, NoThrow carrying a typed 404, and the permission ask → get → reply lifecycle proven end
to end with a configured agent (research Q145). **The B-1 mechanism batch is
complete** (research Q146): the wire-shape wall admits bodyless POST and body-carrying PUT,
and its fifteen operations are landed — the profile stands at **82 selected / 38 pending**.
The remaining mechanism batches follow — inline promotion, envelope extensions, and the query
walls, each design-first — and the **M4 launcher** rides alongside as a small
independent arc (1–2 page plan, approval before source). An **early prerelease packaging track**
accompanies the breadth push: amend the deliberate partial-operation packing wall in
`Directory.Build.targets` so prerelease packs are allowed while stable packs stay blocked
(decision-first; it revises the wall recorded here and rides ADR-0006/#51), then stand up the
ADR-0006 pipeline — per-merge GitHub Packages CD and the manual NuGet.org lane — whose CI legs
can now validate against the restored hosted matrix. M4's launcher arc has since landed (below).

**The M2 second breadth batch is complete** — the design-prover batch:
`session.remove`/`session.rename` and the `Shells` family
(`list`/`create`/`get`/`remove`/`timeout`) ride the 204 no-content and
`{location, data}` envelope machinery, the deepObject `LocationSelector` query channel,
the merged body+query request models, the ambient options location riding the
middleware headers, and the first PATCH/DELETE verbs. `message.list` now carries the pinned query
shape without a description-derived order+cursor refusal. Demonstrated live 2026-08-16 against
`opencode2 serve`
v0.0.0-next-17403 (shell create → get → timeout → remove and session rename → remove,
204s typed, ambient location echoed). `v2.shell.output` deferred to a later batch —
its inline data object and integer cursor query params each need a mechanism of their
own.

**The continuous protocol coverage program is sealed** (maintainer, 2026-08-26; design:
`superpowers/specs/2026-08-26-continuous-protocol-coverage-program-design.md`; decisions and
fact-finding: research log Q148, re-measurement Q149). ADR-0020/0021/0022 record the decision
set; ADR-0003/0005/0007/0008/0013, `spec/SNAPSHOT.md`, `CONTEXT.md`, and the
protocol-and-generation canon are revised in place. The upstream SSE restore is offered as
[anomalyco/opencode#45182](https://github.com/anomalyco/opencode/pull/45182) (open,
`needs:issue`); #56's pin hold converts into a Restore patch, so the refresh no longer waits on
upstream. Re-measured at tip `6170221e` (98 commits past the doc 21 re-check): `contentSchema`
is still 0, the same 31 operations refuse, the two operations upstream added both bind green,
and the restore step still works (326 components). The location design is sealed — ambient plus
typed per-call `LocationSelector`, member-by-member merge — closing #37. ExperimentalDeferred
was dropped; persistentPty is ordinary target surface. The reordered queue was (1) the minimal
`refresh-spec` synchronizer and the first accepted (Restore-patched) refresh — the M5 opener;
(2) the typed per-call location plus normal-PTY ownership lane (ADR-0021); (3) envelope
completion; (4) the operation inventory and assurance ledger; (5) M4's launcher/fixture arc
with the deterministic simulated-model session workflow (ADR-0022). **Lanes (1) and (2) have
landed, so the queue now opens at envelope completion.** The B3/B4 refactors, the
deferred default-job benchmark, and the prerelease packaging track ride alongside unchanged.
Canon-mechanics text (synchronizer internals, location runtime semantics, the assurance
architecture document, quality-gate additions) lands with its implementing increments.

**The synchronizer and the first accepted refresh are complete** (plan fully executed and
retired 2026-08-27; decisions and evidence: research log Q150). The ingestion pre-work landed
first — header parameters ingest behind a selected-operation binder wall, `contentEncoding`
strings project as the fail-closed `EncodedStringNode`, and reason-bearing `operationIdentities`
curation rows map upstream identity defects at ingestion with stale-row retirement — then the
minimal `refresh-spec` synchronizer (prepare/verify/apply over receipts) with the hash-pinned SSE
Restore patch authored from PR #45182's source-only subset. Applying the first receipt took the
accepted snapshot to `954cdc7b`: the question family and its two shipped operations left the
surface, `pty.connect.token` deselected (its new header parameter announced itself at the binder
wall, exactly as Q146 predicted), the eight `persistentPty.*` identity rows mapped the leaked
group id, the pin-era `…1` duplicate rows retired, and the restored event tree's `_N` duplicates
collapsed through structurally validated aliases — with the `ProviderState→Form.Metadata`
structural coincidence refused by hand as doc 21 O6 warned. Derived names now strip Effect's
encode-side `*Encoded` artifact through `ProjectionArtifactNamePolicy` (maintainer-sealed), so the
PublicApi diff shrank to the real drift; the removal-bearing baseline was reviewed and accepted.
The committed sandbox's standing walkthrough ran live against a server built from the new pin —
24 operations answering as declared, including `session.interrupt`'s new typed 200 on the wire.

**The typed per-call location and hand-written PTY family arc is complete** (plan fully executed
2026-08-27; decisions and live evidence: research log Q151). The arc opened with a
document-identical refresh moving **the accepted snapshot to `803ead32`**, then landed, in order:
`OpenCodeRequestOptions.Location`, merged member by member over the ambient location in
`RequestDecorationPolicy` so a per-call scope reaches every route uniformly (#37's implementation);
a curation-declared **internal-raw emission mode** in the generator plus the internal
document-bounded header channel that carries declared header parameters to the wire — no public
header facility, and the already-queued persistentPty HTTP batch inherits the same mechanism; the
hand-written `PtysClient`/`PtyClient` over those internal raw clients (ADR-0021), whose token door
applies `x-opencode-ticket: 1` internally and never as a caller's argument; a curated
`transportOwned` SHA-256 fingerprint over `v2.pty.connect`'s ingested subtree, since that operation
is never selected — its WebSocket door is hand-written directly over its URL/query construction, so
the fingerprint is the only generation-time check that a refresh reshaping it fails loudly; and
`PtySession`, the family's live working object (`ReadAsync` over `PtyOutputFrame`/`PtyCursorFrame`,
`WriteAsync`, graceful disposal) reached through `PtyClient.ConnectAsync(PtyConnectOptions)` with
its replay cursor and per-call location. **The profile stands at 81 selected / 52 pending** —
`pty.list` and `pty.connect.token` joined it with the family. The full gate is green (2,714 tests),
and the sandbox walkthrough's new PTY leg proved the designed path live: the ticket-less upgrade
carrying only the Basic credential answered `101 Switching Protocols`, exactly one cursor frame
closed the replay, reconnecting at that cursor replayed only what followed it, the latest cursor
replayed nothing at all, and removing the PTY ended the read as a normal close.

**The third receipt-governed refresh is complete** (2026-08-28; evidence: research log Q152). The
accepted snapshot is **`d2ee536c`** — upstream force-updated `v2` past `803ead32`, and the first
non-identical candidate added one operation (`server.experimental.persistentPty.handoff`, pending
through a ninth T3 identity row) and three components (`Session.Metadata` on the session models, a
404 arm on `session.import`, one more stabilize duplicate collapsed by alias). The SSE Restore
patch still applies with byte-identical preimages; PR #45182 remains open. The profile stands at
**81 selected / 53 pending**, the full gate is green at 2,767 tests, and the PublicApi baseline
accepted its three additive `Metadata` properties.

**Envelope completion (C2) is complete** (2026-08-28; plan:
`docs/superpowers/plans/2026-08-28-envelope-completion.md`; full mechanism, selection, and live
evidence: research log Q154). The profile stood at 98 selected / 36 pending of 134 at that point.
The final whole-branch review closed (17/17 approved operations verified; 2 MUST FIX / 7 FIX SOON
/ 9 ACCEPT), its two MUST FIX items landed at `4db95d1` (`LanguageModelsClient.ListModelsAsync`
contract tests; the `IntegrationsClient` factory-guard test), and the held maintainer sitting
closed too (the two residual SW004 warnings adjudicated — one redesigned to await the interrupted
event, `d6f653d`; one kept as a documented impossibility-argument suppression, `abdfb28` — plus
the canon wording at `f625c28`/`bc28f65` and the push). The seven FIX SOON items are queued as
named post-arc work in two touches: a *tools hygiene* touch (the wrapper-shape predicate
duplicated across `SchemaNameResolver`/`EnvelopeFacetBinder` — the one with a real silent-drift
hazard, since a one-sided widening would fall back to a pointer-derived name with no wall to catch
it; the `ComposeRegistry`/`RegistryPlan.PayloadEntries` doc one-liners; the
`SerializerTypeNamePolicy`/`RegistryEmitter` dedupe + nested-recursion unit rows; the
`PayloadTypeName` `Response`-suffix test row; the `AssertMissingDataFailsAsync` rename; the
`ToolJsonContext` `NewLine` pin so receipts and the generation manifest stop being written with
mixed line endings on Windows, research log Q155) and a
*vcs.status contract top-up* (empty-list, location-query, protocol-failure tests). The benchmark
coverage batch (recorded above) and the release track remain queued.

**The no-wall sweep is complete** (S1 telltale, S2 batch A, S3 batch B; 2026-08-29; task briefs
and reports: `.superpowers/sdd/2026-08-29-no-wall-sweep/`). S1 landed the interim bindability
telltale (`.generation-incomplete` now marks every pending operation `[bindable]` or
`[refused: …]`, per the `protocol-and-generation.md` rule below) and found the wall-free pool
larger than the sweep's original 14-operation count assumed (persistentPty family members bind
today but stay routed to their own queued batch regardless). S2 admitted batch A, the seven
operations the telltale confirmed bindable with no wall — `credential.activate`, the new `form`
family (`form.request.list`), the `integration` breadth (`list`, `connect.key`, `oauth.connect`),
`project.update`, and `workspace.destroy` — one commit per family, each with its own contract
tests and gate. S3 admitted batch B, the session seven — `session.environment`,
`session.form.create`, `session.form.get`, `session.form.reply`, `session.form.state`,
`session.messageUpdate`, and `session.view` — all landing on the existing bound `SessionClient`
with no curation needed beyond correcting the `session.form.list` naming reason's future-tense
wording now that `session.form.create` is selected alongside it in the same client family with
no collision. The profile stands at **112 selected / 22 pending**, closing the sweep's full
14-operation pool.

**The fourth receipt-governed refresh is complete** (2026-08-29; evidence: research log Q155). The
accepted snapshot is **`106629aa`** — a fast-forward of `d2ee536c` this time (120 commits, four
touching `packages/protocol`), adding two operations, both pending and both marked `[bindable]`
by the telltale: `server.experimental.persistentPty.read` (session-keyed, through a tenth T3
identity row) and `v2.vcs.base`; three components (`PersistentPty.ReadResult`,
`PersistentPty.ReadLinesEncoded`, `Vcs.Base`); `Vcs.Mode` gains `committed`; `vcs.diff` gains a
`base` query and a 503 arm; two duplicated-`anyOf` 404 arms simplify to one `$ref`.
Generated source did not change and the PublicApi baseline is untouched. The SSE Restore patch
still applies with byte-identical preimages and PR #45182 remains open; upstream's #45969
refreshed its committed baseline, so raw equals generated baseline at the tip and the T7 report
retires unfiled. The profile stood at **112 selected / 24 pending** at the refresh, the full gate
green at 3,530 tests.

**M4's launcher arc is complete** (2026-08-28; evidence: research log Q153; plan executed
task-by-task with independent reviews and a clean final whole-branch review).
`OpenCodeServer.StartAsync` is the landed standalone door (ADR-0001) with real-process
three-TFM-plus-net472 lifecycle acceptance on Windows, the exact-pin `PinnedOpenCodeServerFixture`
over a CliWrap control adapter, the repository-owned `DriveController` for the simulation
backend, and the day-one-blocking deterministic simulated-session workflow test (ADR-0022) whose
config-seeded-provider checkpoint passed live (`llm.request` observed with the seeded model id).
The committed sandbox's `--standalone` demo ran live: the SDK started the pinned server itself
and health answered with the child's own pid. Canon carries the three-connection-mode door
taxonomy; CONTEXT.md carries the vocabulary. **The three-OS hosted matrix proof is closed**: run
`33221482103` at `4bf0c07` passed Linux, macOS, and Windows after one budgeted fix round — CI's
"Detect slop" step lacked the canonical `--exclude ".scratchpad/**,external/**"` args, and the new
bun leg's vendored `external/opencode/node_modules/node-gyp/lib/Find-VisualStudio.cs` tripped
SW003 ×4; the named pre-push risks (`FileShare.None` on Unix, bun on hosted runners) did not
materialize. Upstream-report candidate discovered: `session.idle` is deprecated at the pin with no
publisher while `SessionIdle` remains in the event union. **The service-parity follow-up arc
remains queued** (Milestones, M4).

**The persistent PTY family arc is complete** (2026-08-29; plan:
`docs/superpowers/plans/2026-08-29-persistent-pty-family.md`; decisions and live evidence: research
log Q156). ADR-0021's ownership pattern now covers both PTY families: `persistentPty` is a curated
`internalRaw` group keyed on `ptyID`, its ten HTTP operations selected behind the hand-written
`PersistentPtysClient`/`PersistentPtyClient` doors, and `v2.persistentPty.connect` is
fingerprint-pinned transport-owned beside `v2.pty.connect` — **the profile stands at 122 selected /
12 pending / 2 transport-owned** of 136, with the full gate green at **4,016 tests**. The mechanism
the family needed landed first: a `contentEncoding: base64` string now materializes as
`ReadOnlyMemory<byte>` and every other encoding refuses, which is what `snapshot`'s `checkpoint`
required. `PtySession`'s socket lifecycle became the family-neutral `TerminalSocketCore<TFrame>`
behind three named seams — decode, close, and upgrade failure — with the normal family's public
surface unchanged, and `PersistentPtySession` rides the same core: `ConnectAsync` returns only
after the server's `attached` frame and exposes it, output and checkpoints are bytes, input is the
`input_protocol=1` framed layout with viewport tracking, the closed frame hierarchy carries an
unknown control type rather than failing the read, and 4404 covers "no such terminal" and "no
daemon" alike. Test reach grew with it: the exact-pin fixture gained an external-endpoint mode,
`PersistentPtyDaemonGate` picks the live test's arm, and the sandbox README records the WSL2
recipe, since corrected against a real run. **The arc is integrated and the round trip is proven:**
it merged fast-forward into `master` (`203595c..2801dd4`, pushed `66dba42..2801dd4`), and the live
test's round-trip arm executed on hosted Linux and macOS in run `33276305571` and against a WSL2
server from this workstation, carrying the same `arm=round-trip` evidence line each time (Q156);
Windows took the daemon-absent arm, as designed, and every `PersistentPty*` class passed there. That
run's Windows job was red for an unrelated reason — server-process tests scheduled in parallel
stalled the net472 host under two timing-bounded tests, fixed by serializing them (Q157). The
`handoff` door's promoted-body accessor is **closed**: the single-key envelope facet flattens it,
so a caller reads `response.Handoff` as `PersistentPtyHandoff?`.
The `PtySession` read ladder was rerun across the extraction (`--job short`, both runtimes) and the
shared core's added interface dispatch costs no allocation: `DecodeFrames` is byte-identical on
every fixture and runtime, and `ReadFramesAsync` allocates 0–16 bytes less per read (Q156).
The coverage-to-full arc lands its first curation-only admissions at the pinned `b1e3a7b2`
snapshot: `v2.vcs.base` (`VcsClient.GetBaseAsync`) and the filesystem family's `v2.fs.list`
(`FileSystemClient.ListEntriesAsync`).
The query-parameter mechanism then admits required, non-nullable, and enum-valued query
parameters — a required parameter becomes a C# `required` non-nullable request property and makes
`request` a required method and route argument, an optional parameter binds the same way whether or
not its schema admits null, and a query enum binds to a generated C# enum whose wire spelling the
route builder writes through a generated switch — selecting `v2.fs.find`
(`FileSystemClient.FindEntriesAsync`), `v2.vcs.diff` (`VcsClient.GetDiffAsync`), and
`v2.session.stats` (`SessionsClient.GetStatsAsync`) and taking the profile to **127 selected / 7
pending / 2 transport-owned**.
The location envelope's last nominal-only arm then closes: a location wrapper whose `data` is an
inline object is promoted into a model the name resolver claims from the operation
(`{stem}Data`), exactly as the data wrapper's member and the location wrapper's list item already
were, so no upstream wrapper spelling reaches the surface. It selects `v2.shell.output`
(`ShellClient.GetOutputAsync` returning `ShellOutputResponse.Output`, a `ShellOutputData` beside
the location echo, with `cursor` and `limit` riding the query mechanism as the strings the wire
declares) and takes the profile to **128 selected / 6 pending / 2 transport-owned**.
The error model then admits its second wire dialect beside Effect's `_tag`: a `{name, data}`
error binds under the same `IOpenCodeError` base with the `name` literal as its `Tag` (serialized
under `name`) and its `data` struct as a nested generated model, the generated converter scans
`_tag` first and then `name` without a JSON DOM for known arms, and an error dialect the policy
does not name is still refused. The wire-shape wall widens with it: a DELETE that declares a request body now binds and sends it
(RFC 9110 admits DELETE content where the origin server declares support, and the pinned document
declares it), while GET keeps refusing a body and HEAD stays refused at ingestion. Together these
select the worktree family's whole pinned surface — `v2.worktree.create`
(`ProjectWorktreesClient.CreateWorktreeAsync` returning `WorktreeCreateResponse.Worktree`),
`v2.worktree.refresh` (`RefreshWorktreesAsync`) and `v2.worktree.remove`
(`RemoveWorktreeAsync(WorktreeRemoveRequest)`, both 204 no-content envelopes) — giving
`WorktreeError` and `WorktreeErrorData` and taking the profile to
**131 selected / 3 pending / 2 transport-owned**.
The stabilize-duplicate collapse then becomes mechanical: `StabilizeDuplicatePolicy` folds every
reachable `<base>_<N>` component into `<base>` when `SchemaNodeComparer.DeepEquals` holds and
refuses naming both keys when it does not, running to a fixpoint and never chaining. All 24 `_N`
`schemaAliases` rows retire against byte-identical generated output, a curated row the collapse
already implies is refused as redundant, and `.generated-manifest.json` carries the folds in an
`implicitAliases` section as the committed telltale.

## Milestones

Deliverable-first: every milestone ends in something callable or demonstrable. The next
milestone gets a short (1–2 page) plan when it starts — never earlier. Ordering beyond M2
is revisited at each milestone boundary.

1. **M1 — Walking skeleton.** `v2.health.get` + `v2.session.message` end to end
   (SpecDocument → Binder → EmitPlan → Roslyn emitters → committed source under
   `src/OpenCode.Sdk` → minimal transport core → callable client), demonstrated once by
   hand against a real `opencode2 serve` with the output pasted into the PR. Arc B opens
   with the v2 retarget task (pin snapshot, ingestion-wall admit rule, regenerated
   closure). Two independently mergeable arcs: selected compiler + committed models
   (landed), then the callable client with typed errors and `NoThrow`.
2. **M2 — Breadth batches.** The generation profile grows in vertical operation batches;
   each batch lands its curation rows, reachable models, operation methods, and contract
   tests together. The first batch (list/get/create/message-list) is complete with every
   review rider resolved (#19–#22, #20, #25), and so is the alignment batch (uniform
   `*Request`, feature-slice layout, Extensions bring-up — research log Q83–Q85). The
   Extensions package grows in parallel with the remaining batches.
3. **M3 — Streams.** The Q92
   construction reshape (ADR-0010) opens the runway as its own PR. The **location +
   merged-Request design session** (sealed proactive 2026-08-14, research log Session 22;
   census in research doc 15 §5a/§6) seals the marshalling surface. Then the SSE engine
   over the v2 stream surface (`v2.event.subscribe`, `v2.session.log` with
   `after`/`follow`, cursor-paged `v2.message.list`); the v1 durable-stream design does
   not carry over and is re-derived here. Demo: watching a real session's event stream.
   The ADR-0013/0014 authority/materialization cleanup and surviving review findings closed before
   the live-bus breadth step; #44 closed with that selection. Arc 4's paginator and Arc 5's
   owned-transport/net472 GA gate (#43 plus #32) are complete. The union single-pass deserialization
   and streaming adapter-boundary redesign (#23), #29's surviving success-body cost, #33's carrier
   refusal, and the generated collection comparison completed in Arc 6 at `fa6124d`. M3 is complete.
4. **M4 — Launcher and process truth.** The SDK targets full parity with upstream's three
   connection modes under upstream's own vocabulary (maintainer, 2026-08-28): standalone start,
   explicit endpoint, and the registration-file background service
   (`Service.discover/ensure/stop`). M4 itself lands the standalone door —
   `OpenCodeServer.StartAsync` with three-OS acceptance (ADR-0001) over the measured stdio
   contract: `serve --stdio --port 0`, JSON readiness, caller-supplied lease credential via
   `OPENCODE_PASSWORD`, stdin-EOF ownership, and bounded tree termination (research log Q148) —
   plus the explicit-endpoint health/version validation option. Ownership is structural: only
   the started server's working object can end a process, and only its own. Carries the TUnit
   exact-pin server fixture, the deterministic simulated-model session workflow with its
   repository-owned C# controller (ADR-0022), and the net472 stdout/tree-kill items. Demo: the
   SDK starts the server itself and calls health. **The background-service parity follow-up**
   (`OpenCodeService.DiscoverAsync/EnsureAsync/StopAsync` over the registration file — an
   upstream-observed contract outside the OpenAPI pin, canary-guarded) **is its own queued arc
   after M4**; whether read-only discovery rides M4 is decided at the M4 plan review.
5. **M5 — Full surface.** Opened with the minimal `refresh-spec` synchronizer and the first
   accepted (Restore-patched) refresh to the current tip (ADR-0020), then the typed per-call
   location plus normal-PTY ownership lane (ADR-0021) — both landed, with the operation-identity
   rows and the header/base64 ingestion shapes riding along. Envelope completion has landed, and
   so has the interim bindability telltale (S1, 2026-08-29): `generate` marks each pending
   operation `[bindable]`/`[refused: …]` in the committed `.generation-incomplete`, bridging until
   the inventory lane's standardized tracking. What remains is the rest of target admission over
   the refreshed surface: the no-wall sweep's 14-operation pool the C2 probe surfaced is closed
   (S2 batch A and S3 batch B both landed) and so is the persistentPty family, so what is left is
   the remaining mechanism batches, the curation-only trio (`vcs.base`, `config.get`, `fs.list`),
   exclusion fingerprints (ADR-0008),
   the operation inventory and assurance ledger — whose design also standardizes
   pending-operation bindability tracking, so a wall-free pending operation surfaces as a
   committed-artifact diff at every generate/refresh instead of accumulating unseen (maintainer
   requirement, 2026-08-28), subsumes `tools/generation-profile.txt` as the one hand-authored
   admission list (Q148), and makes per-operation assurance mechanically complete (maintainer
   requirement, 2026-08-29): every selected operation carries a contract test for every status
   arm the pinned document declares, checked by an `opencode-tool` verifier that fails the gate
   on a missing arm; every deterministically reachable arm carries exact-pin real-server
   evidence (the fixture, plus the simulated model where a model is needed); arms no
   deterministic fixture can reach are listed by name with their reason, never skipped
   silently (ADR-0022); runtime edges (cut streams, timeouts, malformed bodies, undeclared 3xx)
   stay pipeline-plane tests and are not repeated per operation — remaining ingestion/binding
   walls (#52/#53), and package/API/TFM assurance (#51), packaging unblocked.
   The location design is sealed (#37 closed — ambient plus per-call, research log Q148), so
   the freeze review inherits a settled surface.
6. **M6 — Operational closure.** The observation lanes' automation (tip detector, candidate
   refresh), retry/telemetry/hooks with the public network-timeout knob and optional
   total-budget mode decisions (research Q129/Q133), quarantine lane, the nightly source-run
   canary (ADR-0022;
   the performance suite joins it), and Restore-patch retirement; durable decisions distill
   into ADRs and the remaining `superpowers/` documents retire. Any hygiene-sweep leftovers
   (#24) are resolved here — nothing from the review queue survives the M series.

## Open Questions

- **v2 GA watch** — the v2 line ships as `opencode2` (npm `@opencode-ai/cli@next`, desktop
  beta via `update.opencode.ai`) with no GA date; the spec pin stays a deliberate snapshot,
  refreshed at milestone boundaries. Platform detail: research doc 15.
- **`v2.session.log` resume guarantees** — the pinned OpenAPI exposes `after` as an optional
  string. Upstream implementation source decodes it to a non-negative aggregate sequence, but
  ADR-0013 forbids importing that hidden type through curation. Keep the generated surface
  faithful and use the projection-fidelity audit below to seek an upstream contract fix;
  retention/replay guarantees also remain unestablished (research doc 02).
- **OpenAPI projection fidelity** — measured 2026-08-26 (research doc 21) and re-verified at tip
  `6170221e` (research log Q149); the candidate-refresh lane owns the continuing comparison once
  automated (ADR-0020). Confirmed losses are reported upstream (#44911 / PR #45182); seed cases
  are numeric `limit`/`after` decode targets emitted only as strings. Reports stay diagnostic and
  never feed generation or curation (ADR-0013, research Q107/Q108).
- **Parked upstream reports (doc 21 C4)** — filed when the maintainer chooses, not on a schedule:
  the ten off-convention `persistentPty.*` operationIds (T3 — draft ready under
  `.scratchpad/upstream-issue-drafts/`), the missing `HttpApiSecurity` declaration behind
  `security: []` (T2), the 25 lost `Config.Info` descriptions (T6), and the undeclared
  `x-opencode-ticket` value. T7 (the stale committed document) resolved upstream at `106629aa`
  and needs no report.
- **Release mechanics** — decided parts live in ADR-0006 (independent semver, per-merge
  GitHub Packages CD, manual NuGet.org releases). Pre-1.0 numbering, `VersionPrefix`,
  RELEASE_NOTES flow, and the concrete workflows are scheduled when the first publishable
  increment approaches.
- **A6 configuration/transport split** — deferred with a trigger: when M6 attaches
  telemetry/hook handlers to the transport, or when Extensions gains a concrete
  `IHttpClientFactory`/named-client need, the split of validated client configuration from
  the transport factory lands first (research Q129). Also reopen if the validate-after-owned-transport-construction ordering
  hazard produces a real defect.
- **Generator binding locality** — the doc 18 gate is decided (maintainer, 2026-08-25;
  evidence research Q144): B2's reserved-name owner and B1's facet binders are landed,
  with B3's scenario-derived fixture and B4's error-union extraction sequenced behind
  them.
- **Parent-mediated id access for flat families** — ADR-0019 places single-action
  families as flat id-argument methods. Whether those families should additionally
  expose a handle-style convenience door through their parent client is parked here
  by the maintainer (2026-08-25): adding it later is additive and non-breaking, so
  the evaluation waits until the M5 packaging freeze forces the surface review. The
  same review carries the Azure `dotnet-subclient-properties` follow-up: handle
  clients hold their resource id privately today and exposing it as a property is
  additive.
- **`PtySession.SubmitAsync(string command)` convenience door** — parked (maintainer,
  2026-08-27) until the first real consumer, expected to be this repo's own MCP server
  work: it would type the text and press Enter by explicitly appending `\r` (research
  log Q151: a terminal's Enter key is CR; `\n` alone renders a line without submitting
  it). Adding it later is additive.
- **SDK folder layout review** (maintainer, 2026-08-25) — the family-folder scheme,
  the generated model layer, and statically owned directories share one folder and
  namespace plane guarded only by ad-hoc walls: the `Models` family name was refused
  by the writer's shadow wall (the family became `LanguageModels`), and the stock
  `[Dd]ebug/` gitignore pattern silently swallowed the Debug family's source until a
  narrow negation admitted it. Re-examine the generated layout — folder naming,
  namespace mapping, and the walls that protect them — before the M5 freeze.

## Known Gaps

- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
- **Runtime-arc leftovers, none blocking:** documentation items R04/R05/R06/R11 from the Session
  38/39 review register; net472 pooled rents above the downlevel pool's 1 MB bucket cap still
  allocate one wire-sized copy on >1 MB bodies (a larger-cap `ArrayPool.Create` is a
  benchmark-gated follow-up); the committed sandbox's `--paginate` mode exits nonzero on an empty
  enumeration.
- **`PtySession.ReadCoreAsync` receive-buffer allocation** — the B2 benchmark ladder measured a
  fresh 16 KiB buffer allocated per `ReadAsync()` call (16,776 B on the complete read path vs 24 B
  for decode alone, `cursor-x1`/net10.0): pre-existing, previously unmeasured, now isolated on its
  own rung. Queued as a named, benchmark-gated optimization candidate (an `ArrayPool` rent is the
  obvious shape), not a defect.
- **Hosted net472 host stalls on a server-process start** — the scheduling defect behind run
  `33276305571`'s red Windows leg is fixed and verified (run `33288967119` green on all three legs;
  Q157), but the same report shows that one server-process start still stalls whichever in-process
  tests are running for about ten seconds (three `FailureClassificationTests` cases at 10.2 s,
  milliseconds locally). Harmless now that every timing-bounded test runs alone, and queued as a
  hygiene candidate: the first suspect is .NET Framework's pipe reads, synchronous on a pool thread
  under an async signature, holding thread-pool threads for every piped child. Measure before
  changing anything.
- **Sandbox against an isolated external server** — `dotnet run --project
  tests/OpenCode.Sdk.Sandbox` needs `--no-launch-profile`, because the checked-in
  `launchSettings.json` prefills `OPENCODE_SANDBOX_ENDPOINT` at port 4096; and the walkthrough's
  earlier session legs answer 500 on a provider-less server, so its persistent PTY leg is
  unreachable there. `tests/OpenCode.Sdk.Sandbox/README.md`'s WSL2 recipe was corrected against the
  2026-08-29 run and is done: clone into the WSL filesystem rather than sharing `node_modules` over
  `/mnt/<drive>`, install the pin's own bun, serve under isolated XDG roots, and run the filtered
  live test from Windows.
- **Two curation/doc minors (next curation touch):** the `form` group's curation reason says "no
  per-id operations, ever" where every sibling row states present-tense fact only; the
  `MedianNanoseconds` `compare-benchmarks` CSV column breaks the other columns' abbreviation
  convention.
- **Approved generator/tooling mechanisms (maintainer, 2026-08-29):** (1) *stabilize-duplicate
  collapse* — **landed 2026-08-30**: `StabilizeDuplicatePolicy` folds a reachable `<base>_<N>`
  component into `<base>` when `SchemaNodeComparer.DeepEquals` holds and refuses, naming both,
  when it does not; it runs to a fixpoint, never chains, and records the folds in
  `.generated-manifest.json` (`implicitAliases`) as a committed telltale. All 24 `_N`
  `schemaAliases` rows retired against byte-identical generated output; the one non-`_N` row (the
  operation-scoped Effect cause union) stays explicit, and a curated row the collapse already
  implies is refused as redundant. The manifest lists 25 folds — the 24 the rows spelled plus
  `Form.Fields_1`, an array component that never needed a row because it emits no named model.
  Still open: the research-log entry and one curation-boundary sentence in canon.
  (2) *source watch* — **landed 2026-08-30**: `spec/source-watch.json` pins the 20 upstream files
  the hand-written PTY doors read — 7 for the normal family, 13 more for persistent PTY, including
  the daemon's location and `packages/core/package.json` pin and the three behavioral oracles
  (`packages/server/test/persistent-pty.test.ts`, `packages/client/src/solid/pty.ts`,
  `packages/tui/src/component/terminal-pane.tsx`) — by path, sha256, and one `contains` anchor
  naming the behavior that door depends on. `refresh-spec` prepare observes every entry at the
  candidate commit and records its hash and anchor verdict in the receipt (`watchedSources`),
  verify checks the pins against the submodule checkout and refuses a missing file, a moved blob,
  or a lost anchor, and apply re-pins over the reviewed receipt — refusing a receipt whose anchors
  the reviewer saw fail. A review trigger only, never a generation input (ADR-0013); its stated
  blind spot is behavior added in a file the list does not name, backstopped by the sandbox PTY
  legs today and the M6 canary later. Still open: the research-log entry and the two proposed canon
  sentences (`spec/SNAPSHOT.md` §Refresh procedure, `protocol-and-generation.md` §Snapshot
  production). (3)
  *transport-owned leaves pending* — **landed 2026-08-29**: `SpecBinder` derives pending as the
  unselected operations without a fingerprint-pinned `transportOwned` row (a selected operation
  with such a row refuses), the marker carries a fixed `Transport-owned operations:` count and a
  `Transport-owned:` section while it exists (emitted even when empty, so a row's arrival or
  retirement is a one-line diff), and the canon marker sentence reads "unselected and not
  transport-owned". The marker reads **122 selected / 12 pending / 2 transport-owned**, and the
  packing wall is satisfiable at full admission; the prerelease-versus-stable wording of that wall
  stays a release-prep decision.
