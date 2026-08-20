# Roadmap

Date: 2026-08-20

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
envelopes; the standing baselines live in the M3 plan and are measured against
wire-shaped payloads),
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

**M3 is open** (plan: `superpowers/plans/2026-08-15-m3-plan.md`; decisions: research log
Sessions 24–34). Q92's simplicity-first construction (ADR-0010) is landed — Arc 1 complete:
the transport constructor is internal friend-assembly test surface, Extensions registers
a factory-less singleton client family with a roster contract test, and the Q91 guard
machinery is deleted — closing #31 by construction. #32 and #33 stay sealed with their
execution homes recorded (Session 24), and the location + merged-Request design is
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
baselines remain the comparison guards; master protection and direct-push policy remain open under
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

**Arc 4's paginator is locally complete and awaiting its source checkpoint and hosted matrix.**
ADR-0017 keeps generated `ListMessagesAsync` as the explicit page/cursor/`NoThrow` door and adds
`EnumerateMessagesAsync` as a lazy item sequence over the same virtual method and response adapter.
The binder admits the companion only for the exact `ListRequest` plus cursor-list dialect; the
hand-written core follows opaque `cursor.next`, while generated metadata retains the string `limit`,
omits first-page-only `order` on continuations, and never decodes or compares cursors. Deterministic
evidence covers an unchanged initial order+cursor request, empty intermediate pages, null-only
termination, typed later-page errors, cancellation between buffered items, an empty cursor, and the
mocking seam. The PublicApi review contains one additive method. Research Q117/Q118 own the survey,
decision, implementation, local 1,338-test closure, and a Linux live run where the committed sandbox
enumerated two real historical messages with `limit=1` across two pages without creating data or
invoking a provider. Arc 5's net472 owned-transport GA cluster is next only after this source
increment is committed and hosted green; Arc 6 remains mandatory after Arc 5.

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
3. **M3 — Streams.** Plan: `superpowers/plans/2026-08-15-m3-plan.md`. The Q92
   construction reshape (ADR-0010) opens the runway as its own PR. The **location +
   merged-Request design session** (sealed proactive 2026-08-14, research log Session 22;
   census in research doc 15 §5a/§6) seals the marshalling surface. Then the SSE engine
   over the v2 stream surface (`v2.event.subscribe`, `v2.session.log` with
   `after`/`follow`, cursor-paged `v2.message.list`); the v1 durable-stream design does
   not carry over and is re-derived here. Demo: watching a real session's event stream.
   The ADR-0013/0014 authority/materialization cleanup and surviving review findings close before
   the live-bus breadth step; #44 closed with that selection. Arc 4 paginator is next, followed by
   the net472 owned-transport cluster
   (#43) lands here as a GA
   gate. The
   union single-pass deserialization and streaming adapter-boundary redesign (#23) land
   on the M3 runway, gated on the performance baselines (#18), together with the
   second-review perf mechanisms and #29; #32 (uniform route-boundary refusal) rides the
   net472 cluster and #33 (carrier construction refusal) rides #23.
4. **M4 — Launcher.** `OpenCodeServer.StartAsync` with three-OS acceptance (ADR-0001)
   over `opencode2 serve`; demo: the SDK starts the server itself and calls health. The
   net472 stdout/tree-kill items land here. (`serve --stdio`'s stdin leash and the
   background service's discovery file are candidate mechanisms — decided in the M4 plan;
   platform detail: research doc 15.)
5. **M5 — Full surface.** Complete generation profile over the protocol surface,
   exclusion fingerprints (ADR-0008), remaining ingestion/binding walls (#52/#53), and
   package/API/TFM assurance (#51), packaging unblocked. The **ambient location
   header decision (#37)** lands here and is decision-first: packaging unblocking
   freezes the public surface, so this is the last free moment to drop
   `OpenCodeClientOptions.Location` (option B) or fold it into the query channel
   (option C). The batch admitting `project.list` / `permission.saved.*` /
   `session.form.*` answers whether the header is those operations' only addressing
   channel.
6. **M6 — Operational closure.** `refresh-spec`, retry/telemetry/hooks, quarantine
   lane, nightly canary (the performance suite joins it); durable decisions distill
   into ADRs and the remaining `superpowers/` documents retire. Any
   hygiene-sweep leftovers (#24) are resolved here — nothing from the review queue
   survives the M series.

## Open Questions

- **v2 GA watch** — the v2 line ships as `opencode2` (npm `@opencode-ai/cli@next`, desktop
  beta via `update.opencode.ai`) with no GA date; the spec pin stays a deliberate snapshot,
  refreshed at milestone boundaries. Platform detail: research doc 15.
- **`v2.session.log` resume guarantees** — the pinned OpenAPI exposes `after` as an optional
  string. Upstream implementation source decodes it to a non-negative aggregate sequence, but
  ADR-0013 forbids importing that hidden type through curation. Keep the generated surface
  faithful and use the projection-fidelity audit below to seek an upstream contract fix;
  retention/replay guarantees also remain unestablished (research doc 02).
- **Spec refresh cadence** — the `refresh-spec` tool lands in M6; the cadence policy stays
  open.
- **OpenAPI projection fidelity** — at the next sanctioned refresh and before M5 public-surface
  freeze, independent read-only passes compare current upstream Effect schemas, generated OpenAPI,
  and first-party generated clients. Reproduce and report confirmed losses upstream; seed cases
  are numeric `limit`/`after` decode targets emitted only as strings. Reports are diagnostic and
  never feed generation or curation (ADR-0013, research Q107/Q108).
- **Generated collection representation** — Arc 6 benchmarks direct `IReadOnly*` surfaces against
  `ImmutableArray`/`ImmutableDictionary` across JSON, AOT, downlevel TFMs, request ergonomics, and
  allocation/throughput before M5 freezes the API. `IReadOnly*` remains the default unless total
  evidence favors concrete immutable types (research Q108).
- **Release mechanics** — decided parts live in ADR-0006 (independent semver, per-merge
  GitHub Packages CD, manual NuGet.org releases). Pre-1.0 numbering, `VersionPrefix`,
  RELEASE_NOTES flow, and the concrete workflows are scheduled when the first publishable
  increment approaches.

## Known Gaps

- **`BuildOs`/`BuildArch`** properties are kept in `Directory.Build.props`; adapt their values to
  opencode's release-asset naming when the binary-download need lands.
