# Production walking skeleton: first callable SDK milestone

Date: 2026-08-13

> **Status: vision / reference — not sealed.** Binding decisions live in the ADRs and
> `AGENTS.md`; this document is direction and design rationale, not law. Contradicting it
> is a finding to note, not a deviation-protocol event.

## Decision

The SDK build-out will stop sequencing ingestion, binding, emission, transport, and the
operation surface as five broad horizontal stages before the first callable client. Slice 1
will close as a lean semantic-ingestion module, then two bounded, independently mergeable
production arcs will carry two modern operations through the final pipeline and commit their
generated SDK surface under `src/OpenCode.Sdk`:

- `v2.health.get` — the simple control path.
- `v2.session.message` — the representative hard path: a bound session handle, two path
  parameters, a `{data}` success envelope, an eight-variant marked union, a content array whose
  items form a three-variant marked union, a nested status-marked tool-state union, and declared
  400/401/404 errors.

This is production code, not a throwaway prototype. Every interface and public name starts in
its final architectural role. Later milestones extend the supported operation and schema set;
they do not replace a temporary pipeline or rename the first public surface.

The generator remains our own Roslyn-emission generator over Microsoft.OpenApi. The ingestion
policy changes from exhaustive DOM-member surveillance to semantic-risk fail-closed validation:
generation refuses when an unsupported construct can change emitted wire or public behavior,
not merely because the reader exposes metadata the generator does not consume. Pinned-spec and
generated-source diffs become the primary drift radar, backed by targeted semantic and runtime
guards.

## Evidence

The decision follows two independent real-spec probes:

1. The current ingestion implementation absorbed the complete pin: Microsoft.OpenApi loaded
   162 paths, 472 named schemas, and 188 operations with zero reader errors; projection covered
   all 188 operations and all 472 named schemas with zero projection errors; 23 semantic
   landmarks passed; two fresh runs were identical.
2. Raw Kiota 1.34.1 generated correct basic HTTP plumbing but failed wire fidelity for the
   semantics this SDK promises. A valid `TextPart` response lost its `type` marker and scattered
   fields across unrelated branch objects; a valid `SessionDurableEvent` selected no branch and
   lost the complete payload; an unknown part lost its tag and raw fields; a declared 400 body
   became a generic body-less `ApiException`. A deliberately constructed outbound `TextPart`, a
   simple health response, and raw SSE bytes were preserved.

The probes establish two different conclusions. A semantic projection is load-bearing; the
generic C# generators do not preserve this pin's discriminator-free literal-marker unions.
Exhaustive reflection walls, default-value inventories, full intermediate snapshots, and
pre-consumer hashing are assurance-policy choices, not prerequisites for correct SDK output.

## Goals

- Put a callable, generated `OpenCode.Sdk` surface on the main development line after two
  bounded production arcs rather than after four broad horizontal slices.
- Exercise the actual seam chain: pinned spec → SpecIR → Binder → EmitPlan → Roslyn emitters →
  committed source → transport → generated method.
- Validate both the easy HTTP path and the highest-risk union/envelope/error path before broad
  generator implementation continues.
- Preserve the final public API rules: immutable generated models, required mirroring,
  source-generated System.Text.Json metadata, explicit unknown-variant carriers, typed response
  envelopes, typed errors, throw-by-default, and per-call `NoThrow`.
- Make later work consumer-pulled: add an ingestion fact, EmitPlan decision, runtime behavior, or
  test only when a selected operation or an agreed standing contract consumes it.
- Keep generated output extend-only. Expanding scope adds public types and methods without
  changing the first milestone's names or behavior contracts.

## Non-goals

- Publishing a NuGet package from the partial milestone.
- Supporting all 61 modern and 127 legacy operations in the first callable milestone.
- Retry, telemetry, per-attempt hooks, Extensions DI, launcher, SSE parsing, or real-process
  integration.
- Replacing Microsoft.OpenApi, SpecIR, the Binder/EmitPlan split, or Roslyn emission.
- Adopting Kiota, Refitter, NSwag, OpenAPI Generator, or a second generated runtime surface.
- Weakening runtime unknown-variant tolerance. Generation-time strictness and runtime
  forward-compatibility remain intentionally different regimes.
- Adding temporary public facades, duplicate handwritten operation methods, or compatibility
  shims for code that has not shipped.

## Engineering Policy

### Semantic-risk fail-closed

The generator fails when continuing could emit a wrong request, wrong response model, wrong
serializer, wrong route, or misleading public contract. Examples include:

- Reader diagnostics, reader crashes, or an OpenAPI version outside the accepted version.
- Unresolved references and unsupported `$ref` siblings whose semantics cannot be retained.
- Ambiguous or unsupported union shapes in the selected reachable closure.
- Path-level parameters, parameter encodings, request/response media shapes, response headers,
  callbacks, or other protocol constructs that the selected operation would otherwise drop.
- Unknown response content types that have no payload mapping.
- Missing literal markers for a union that requires tag dispatch.
- Any selected operation, response, or schema that cannot produce the promised immutable,
  AOT-safe SDK shape.

The generator does not fail solely because:

- Microsoft.OpenApi adds a public property or changes a fresh-instance default that no emitted
  rule reads.
- A document contains descriptive or organizational metadata the SDK intentionally ignores.
- An unrecognized vendor extension has no declared generator semantics; it is reported as a
  located informational drift entry rather than silently disappearing.
- A legal construct exists outside the selected milestone scope and cannot affect its output.

The last case is a staged-build tolerance, not permanent best-effort generation. The final
generation profile selects both API surfaces; a construct reachable from that full profile must
either be supported or fail with a located diagnostic.

Extension handling has three explicit dispositions. `x-effect-stream` and `x-websocket` carry
declared semantics. `x-codeSamples` is known-ignored descriptive input. Any other `x-*` value is
listed with its location in the generation report; it becomes fatal only when a projection,
Binder, or runtime rule would otherwise lose behavior it claims to emit. This keeps refresh
review informed without restoring an exhaustive extension allowlist.

### Git-led drift detection

A spec refresh is reviewed through four evidence layers:

1. The pinned `spec/openapi.json` diff.
2. The committed generated-source diff.
3. Targeted semantic tests for known lossy seams.
4. Build, analyzer, AOT, and runtime contract gates.

This intentionally replaces a full reflection inventory of every reader DOM member and a full
SpecIR snapshot. Git cannot detect a construct silently ignored by the generator when no output
changes, so targeted validators remain at known lossy seams. Git is the primary radar, not the
only radar.

### Consumer-pull rule

Every SpecIR field and every persistent validation mechanism must name a downstream consumer or
an observed failure it prevents. A fact needed only by a later feature is added in that feature's
milestone. A test that protects neither an observed failure nor a standing public/wire contract
is deferred.

## Lean Slice 1 Contract

Slice 1 still produces one deep module: `ISpecIngestion` returns an immutable `SpecDocument`
whose public surface contains no Microsoft.OpenApi types. Microsoft.OpenApi remains confined to
the ingestion implementation.

### Keep

| Capability | Reason |
|---|---|
| Reader, version gate, diagnostics, exception translation | The maintained parsing and hard-failure base |
| Schema and operation semantic projection | Proven necessary by the Kiota wire-fidelity probe |
| Primitive, object, dictionary, free-form, unrestricted, enum, literal, array, tuple, ref, nullable, special-number, content-string, and union nodes | Required by the real pin and later emitters |
| Marked versus structural union classification | Determines converter versus structural representation |
| Literal marker and error-style facts | Drives known/unknown dispatch and typed HTTP errors |
| Modern/legacy surface, route, method, parameters, media, envelope, SSE/WebSocket, and documentation facts | Drives operation and response emission |
| Deterministic graph keys and stable ordering | Keeps generated diffs local and repeatable |
| Raw `$ref` sibling detection and dangling-reference validation | Prevents reader proxy behavior from hiding wire semantics |
| `prefixItems` adapter | Required by the pinned `Config.plugin` tuple |
| Batched located diagnostics for actual semantic failures | Makes generator refusal actionable |
| Boundary guard preventing Microsoft.OpenApi leakage | Preserves the deep module seam |

### Remove or defer

| Capability | Disposition |
|---|---|
| Generic reflection `HostMemberWhitelist<T>` over every public property/default | Replace with explicit checks tied to emitted semantics |
| Full DOM member/default inventory baseline | Remove |
| Full pinned SpecIR Verify snapshot | Remove; generated source is the reviewed artifact |
| Raw hash for every operation and named schema | Defer until excluded/hand-wired fingerprint persistence has a consumer |
| `SpecOperation.RawContentHash` and document-wide schema hash fields with no current consumer | Remove now; add with the fingerprint feature |
| Fatal handling of every unknown `x-*` extension | Replace with located informational reporting unless the extension affects emitted behavior |
| Exhaustive landmark inventory | Reduce to representative lossy seams and final-scope smoke |
| Facts or collaborators with no Binder/emitter/refresh consumer | Delete or defer after the consumer audit |

Existing schema and operation projectors are not presumed disposable. The simplification review
uses the deletion test: if removing a module spreads union, route, media, or normalization logic
across Binder rules, the module is earning its keep. Assurance-only machinery that disappears
without moving correctness complexity elsewhere is removed.

The standing targeted ingestion gates are explicit: OpenAPI version and reader diagnostics;
`prefixItems` still reaching the supported raw-key adapter; `$ref` sibling and dangling-reference
behavior; no Microsoft.OpenApi type outside the ingestion seam; deterministic graph keys; and
pinned known/unknown union, unrestricted-schema, SSE-metadata, and both-surface landmarks. A new
gate requires a concrete lossy seam or standing public contract.

## Operation Selection

The Binder consumes an internal immutable `OperationSelection`. Selection is a final generator
capability used by focused emitter tests, diagnostics, and staged repository generation; it is
not a public SDK concept.

A checked-in generation profile under `tools/` supplies the repository's current selection. The
first profile contains exactly:

```text
v2.health.get
v2.session.message
```

Selection rules are fail-loud:

- Every listed operation ID must exist exactly once.
- Duplicate IDs refuse.
- The selected schema set is the transitive closure of every selected parameter, request body,
  success/error response, nested property, and union branch.
- An unselected operation is pending breadth, not a product exclusion; it receives no exclusion
  fingerprint and does not appear in the public SDK.
- The generation report names pending modern and legacy operation counts so partial scope cannot
  masquerade as completion.
- Before any package release, the checked-in profile moves to both complete surfaces and the
  full-coverage gate becomes mandatory.

The generator reports three disjoint operation sets:

| Set | Meaning | Fingerprint policy |
|---|---|---|
| Selected | Emitted by the current profile | Generated source and contract fixtures are the drift radar |
| Pending | Present in the spec but intentionally outside the pre-release staged profile | No fingerprint; count and IDs are reported, and packaging is blocked |
| Excluded or hand-wired | Inside the intended full product surface but deliberately not emitted as an ordinary method | Fingerprint required before the first member enters this set |

Pending is a build-out state, not a product policy. The first operation cannot move from pending
into the excluded or hand-wired set until fingerprint persistence is implemented and verified.
Moving the profile to full breadth is likewise blocked on the fingerprint manifest.

Partial breadth is machine-enforced. The Writer owns a partial-generation marker beside its
manifest whenever pending operations exist. `dotnet pack` fails while that marker exists, CI
asserts that the marker agrees with the profile and report, and `generate --verify` verifies the
marker as an owned output. Full breadth removes it. Operational prose is not the release guard.

`curation.json` remains sparse public-API policy. Operation selection and curation are separate:
selection answers what is emitted in the current milestone; curation answers how selected
constructs map to the final public API.

Layer-1 curation coverage is selection-scoped during staged generation:

- Spec-to-curation checks cover selected operation IDs, their groups, envelope payload names,
  content types, null-semantics decisions, and reachable override sites.
- Curation-to-spec checks remain global: every row must name a real construct in the pin.
- A row that targets a pending operation is rejected until that operation becomes selected, so
  speculative curation cannot accumulate.
- The full profile restores spec-to-curation coverage over both complete surfaces before
  packaging can be enabled.

The selected-scope curation change is a public API review even though the package is not yet
published.

## Walking-Skeleton Pipeline

```text
spec/openapi.json
    │
    ▼
ISpecIngestion ───────────────▶ SpecDocument
                                  │
                     OperationSelection + sparse curation
                                  │
                                  ▼
                              Binder
                                  │
                                  ▼
                              EmitPlan
                                  │
                    ┌─────────────┴─────────────┐
                    ▼                           ▼
             Roslyn emitters                 Writer
                    │                           │
                    └─────────────┬─────────────┘
                                  ▼
                         src/OpenCode.Sdk
                                  │
                   generated methods delegate once
                                  ▼
                    Pipeline.ExecuteAsync core
                                  │
                                  ▼
                              HttpClient
```

The Binder is the only module that knows both wire semantics and C# public-policy inputs. The
emitters do not read Microsoft.OpenApi or SpecIR. The runtime does not know the spec, Binder, or
EmitPlan.

### Binder and EmitPlan scope

The first Binder implementation is complete for the selected closure, not a mock. It owns:

- Reachable-closure computation.
- Mechanical naming and identifier mapping.
- Root method and bound-session-handle placement.
- Envelope payload naming.
- Known union converter and unknown-carrier decisions.
- Error hierarchy and status mapping.
- Source-generation registry membership.
- XML documentation inputs.

An unsupported selected fact produces a categorized Binder error. The Binder never falls back to
raw Microsoft.OpenApi access and never silently asks an emitter to infer wire semantics.

EmitPlan types are final role-specific instructions. They carry only what an emitter consumes.
The first milestone may not instantiate every future plan subtype; it must not add placeholder
fields for hypothetical emitters.

### Emitters and Writer scope

The milestone implements the final emitters required by the selected closure:

- Immutable models and literal enums.
- Marked-union bases, known variants, explicit unknown carriers, and custom converters.
- One source-generated `JsonSerializerContext` registry.
- Typed success/error envelopes and input types required by the selected operations.
- Root client, sessions collection, bound `SessionClient`, routes, and operation methods.
- XML documentation and analyzer-clean source construction.

The Writer owns the final output manifest, stale-file deletion within its owned roots,
deterministic writes, and the formatting post-step. `generate` becomes real in this milestone;
`generate --verify` regenerates the selected profile and fails on a diff.

Raw `$ref` sibling detection does not depend on hashing. A dedicated raw-key scanner walks schema
positions and reports siblings independently; the later hashing implementation may share the
same traversal abstraction but is not required for correctness.

## Runtime Core

The first callable milestone implements only the final behavior needed to prove a real call:

- Standalone endpoint construction and BYO `HttpClient` construction.
- Correct `HttpClient` ownership for those paths.
- `OpenCodeClient.Dispose` disposes the self-created `HttpClient` and never the injected client.
- Basic authentication resolves explicit `Password` then the environment fallback at client
  construction. User-Agent and client/per-call `x-opencode-directory` decoration are active.
- The sealed public design's shared `Pipeline.ExecuteAsync<TResponse>` behavior core used by
  one-line generated methods; it is not a consumer-extensibility policy framework.
- Route expansion, request headers/content needed by the selected operations, cancellation, and
  response disposal.
- Source-generated JSON serialization/deserialization with no reflection fallback.
- Typed success envelopes.
- Declared HTTP error parsing into the generated `OpenCodeError` hierarchy.
- Throw-by-default via `OpenCodeApiException` carrying status and typed error data.
- Per-call `OpenCodeRequestOptions.NoThrow`, returning an error envelope without changing the
  method's return type.

Retry, telemetry, hooks, DI Extensions, launcher, raw escape hatch, and streams remain later
runtime breadth. The first milestone does not expose switches whose behavior is not implemented.
Adding those capabilities later extends the shared behavior core without changing generated
method bodies.

## Public Surface

The selected modern health group is curated as a single-operation root group, so it produces a
root client method. The session group curation declares `{sessionID}` as its handle parameter,
so the selected message operation lives only on the bound session handle. The intended shape is:

```csharp
var client = new OpenCodeClient(new Uri("http://localhost:4096"));

HealthResponse health = await client.GetHealthAsync(cancellationToken: cancellationToken);

SessionClient session = client.Sessions.GetSessionClient(sessionId);
SessionMessageResponse response = await session.GetMessageAsync(
    messageId,
    options: requestOptions,
    cancellationToken: cancellationToken);
```

Exact generated type names follow the existing naming and curation rules, but these structural
decisions are fixed:

- `GetHealthAsync` is a root method.
- `GetMessageAsync` exists only on `SessionClient`; no duplicate flat overload is emitted.
- Every method is generated and delegates in one line through `Pipeline.ExecuteAsync`.
- Every operation returns a named typed response envelope.
- Client/root/sub-client methods use the final virtual-member mock seam.
- Models, options, exceptions, envelopes, and non-client generated types remain sealed where the
  public design requires it.

The partial milestone is not published. Its public source still follows extend-only rules so
scope expansion can only add members and types.

### First callable public inventory

The runtime arc lands only public members whose behavior is complete:

| Type | Members present in the first callable arc |
|---|---|
| `OpenCodeClient` | `IDisposable`; standalone `Uri`, `Uri` + options, and BYO `HttpClient` + options constructors; root `GetHealthAsync`; readonly `Sessions`; final protected mock seam |
| `OpenCodeClientOptions` | `Endpoint`, `Password`, and default `Directory`; basic authentication and directory decoration are implemented when these members land |
| `OpenCodeRequestOptions` | `ErrorBehavior`, per-call `Directory`, and static `NoThrow` exactly as the public API design specifies |
| `ErrorBehavior` | `Default` and `NoThrow` values used only through per-call request options |
| Exception spine | Hand-written `OpenCodeException`, `OpenCodeApiException`, and `OpenCodeTransportException` with their final inheritance and payload roles |
| Response spine | Hand-written `OpenCodeResponse` base with status, `IsError`, typed error, raw error body, and guarded success behavior |
| Generated clients | `Sessions`, bound `SessionClient`, `GetHealthAsync`, and `GetMessageAsync` only |
| Generated data | Selected success/error models, envelopes, marked unions, unknown carriers, converter set, routes, and serializer context |

`Retry`, delegate hooks, logging plumbing, and their option members do not exist until their
behavior lands. Adding those option properties later is the already-sealed extend-only growth
model. The raw `SendAsync` escape hatch also remains absent until its behavior is implemented.

## Serialization and Errors

### SessionMessage

`SessionMessage` is an eight-variant marked union keyed by the required `type` literal. The
assistant variant's `content` is an array whose items form a three-variant marked union keyed by
`type`. Its tool branch contains a four-state marked union keyed by `status`. The generated
converter set:

1. Buffers one JSON value.
2. Reads `type` independently of property order.
3. Dispatches known values through the generated context.
4. Produces `UnknownSessionMessage` for an unrecognized tag, carrying the tag and raw
   `JsonElement`.
5. Re-serializes both known and unknown values without semantic data loss.

The converter never uses reflection and never delegates unknown handling to
`JsonUnknownDerivedTypeHandling`, which cannot implement the required deserialization carrier.

The health response's required single-value boolean enum is not used as a polymorphic marker.
The Binder emits it as a required `bool` property so a version-skew `false` value remains visible
instead of being silently rewritten to `true`; the pinned `true` constraint remains a landmark
and contract expectation.

### HTTP errors

For `v2.session.message`, the milestone covers:

| Status | Wire data |
|---|---|
| 400 | `InvalidRequestError` |
| 401 | `UnauthorizedError` |
| 404 | deduplicated `MessageNotFoundError` or `SessionNotFoundError` |

The exception bases and `OpenCodeApiException` are hand-written identity core. Concrete error
models and their unknown carrier are generated. Default calls throw `OpenCodeApiException` with
the status and typed `OpenCodeError`. `NoThrow`
returns `SessionMessageResponse` with `IsError = true`, the typed error populated, and guarded
success payload access. An unknown future error tag becomes the generic unknown-error carrier;
an undeclared status or known tag at the wrong status uses the same carrier rather than widening
the operation contract. The raw body remains available on both the exception and the `NoThrow`
response. A malformed error body preserves that raw body with a null typed error.

Transport failures and cancellation stay separate: network/protocol failures use the transport
exception spine; cancellation follows the BCL `OperationCanceledException` convention.

## Verification

### Generator acceptance

| Check | Required result |
|---|---|
| Pinned ingestion | Selected profile ingests with no semantic errors |
| Scope | Exactly the two operation IDs; pending counts reported honestly |
| Closure | Every referenced success/error/nested union type present; no orphan emitted |
| Repeat | Two generations byte-identical after formatting |
| Verify | `generate --verify` exits successfully on a clean tree and fails on drift |
| Manifest | Owned files listed; stale owned file removed; unrelated file untouched |
| Boundary | No Microsoft.OpenApi type reaches SpecDocument, Binder output, generated code, or runtime |

### Generated-model acceptance

| Check | Required result |
|---|---|
| Known user message | Correct concrete variant and property values |
| Known assistant message | Correct outer variant and ordered content array of text/reasoning/tool variants |
| Property order | `type` and nested `status` markers may appear anywhere in their objects |
| Unknown message | Explicit carrier with exact tag and raw payload |
| Unknown nested content | An unknown content-array item preserves its tag and raw payload |
| Assistant tool state | Known and unknown `status`-marked tool-state variants preserve data |
| Round trip | Known and unknown payloads remain semantically equal |
| Registry | One generated context contains the complete selected closure |
| Reflection | Reflection-disabled runtime probe succeeds |
| Immutability | Records/init/read-only collections and required mirroring hold |

### Generated-client acceptance

All calls use an in-memory recording `HttpMessageHandler` and the real generated client/runtime:

| Scenario | Required result |
|---|---|
| Health 200 | `GET /api/health`; typed `Healthy = true` response |
| Health 400/401 | Every declared health error maps through default throw and `NoThrow` paths |
| Session message 200 | Correct `/api/session/{sessionID}/message/{messageID}` expansion and typed union payload |
| Session message 400 | Typed `InvalidRequestError` on default throw path |
| Session message 401 | Typed `UnauthorizedError` on default throw path |
| Session message 404 | Both not-found variants select the correct subtype |
| `NoThrow` | Every selected declared status returns the same typed error in an error envelope |
| Unknown error | Generic carrier retains tag and raw body |
| Authentication | Explicit password and environment fallback produce the expected Basic authorization header |
| Directory | Client default and per-call override produce the expected `x-opencode-directory` header |
| User-Agent | Every selected call carries the SDK User-Agent decoration |
| Cancellation | Token reaches `HttpClient`; cancellation is not remapped as API error |
| Transport failure | Network/protocol failure maps to `OpenCodeTransportException`, never an API error |
| Client disposal | Standalone client disposes its owned HTTP client; BYO HTTP client remains usable |

### Repository gates

- Generated and handwritten product code builds with zero warnings and errors on
  `netstandard2.0;net472;net8.0;net9.0;net10.0`.
- Relevant unit/contract tests run on the repository's supported test legs.
- Generated output passes the analyzer wall on merit.
- The existing net10+ `IsAotCompatible=true` metadata remains active; a reflection-disabled
  serializer probe is mandatory. A real Native AOT publish smoke is owned by complete operation
  breadth before packaging is enabled.
- `dotnet format --verify-no-changes --no-restore` passes.
- Slopwatch reports no warnings.
- The partial-generation marker is present and verified while operations remain pending;
  `dotnet pack` fails mechanically, independent of operational documentation.
- Level-2 contract breadth is complete for every selected public operation and every declared
  response. The gate expands to both complete surfaces with the full profile.

## Revised Sequencing

### Decision-document alignment

Before production code resumes, update the affected ADRs, architecture/public/testing specs,
slice map, active Slice 1 plan, ROADMAP, and issue graph so they describe this design. The revised
execution plan is reviewed as the gate into the arcs below.

### Lean Slice 1 — ingestion and SpecIR

Close the existing branch under the lean policy. Review the existing Task 7 semantic operation
projection after replacing or removing assurance-only host-wall machinery. Implement minimal
orchestration, targeted boundary/reader guards, and focused pinned-spec smoke. Do not add
document-wide content hashes or exhaustive library-upgrade snapshots.

### Production arc A — selected compiler and committed models

In one independently mergeable PR, implement selection-scoped curation checks, Binder/EmitPlan,
the selected model/union/registry emitters, Writer, partial-profile enforcement, the real
`generate` / `--verify` path, and the selected generated model closure under `src/OpenCode.Sdk`.
The selected sources compile on all five TFMs but expose no nonfunctional client methods.

### Production arc B — callable client and core errors

In the next independently mergeable PR, implement the final exception/response/request-option
spines, the minimal shared behavior core, client/envelope/routes/operation emitters, the first
generated methods, and the selected-operation contract suite. This is the first callable SDK
milestone.

The revised implementation slice map assigns concrete slice and issue numbers to these arcs;
this design does not silently reuse the old issue meanings.

### Vertical breadth expansion

Expand the checked-in profile in bounded operation batches. Each batch carries its complete
reachable model closure through curation, Binder/EmitPlan, generated models and methods, and
level-2 contracts. There is no separate full-model horizontal stage and no orphan model
inventory. The Writer, manifest, registry, runtime delegation, and partial-packaging guard remain
unchanged.

### Transport and Extensions breadth

Add retry, telemetry, hooks, the raw escape hatch, complete options, and
`OpenCode.Sdk.Extensions` DI composition around the existing pipeline. Existing generated method
bodies do not change.

### Complete operation surface

Author full sparse curation, implement fingerprint persistence, expand the generation profile to
all 61 modern and 127 legacy operations, add the remaining operation/envelope/input/routes/
paginator emitters, and enforce full happy-path plus declared error contract breadth. The Native
AOT publish smoke passes and the partial-scope marker disappears before this milestone can
complete.

Launcher, real-process harness, SSE, container, coverage, and operational-closure sequencing can
remain after the complete client, subject to the implementation slice map's dependency review.

## Stop Conditions

Stop and revisit the design if any of the following occurs:

- Supporting `v2.session.message` requires a second wire model outside SpecIR rather than a
  narrow Binder decision.
- The Binder must access Microsoft.OpenApi or raw JSON to recover facts projection claimed to
  provide.
- The first public method cannot use its final placement or signature without speculative
  compatibility code.
- Selected generation needs a temporary emitter or transport path that later breadth would
  replace instead of extend.
- The selected closure cannot compile across the five TFMs without reopening a locked product
  requirement.
- Partial generation can silently present itself as full breadth.
- Either production arc cannot fit one focused execution arc without crossing into the next
  arc's acceptance contract.
- The milestone grows to include retry, telemetry, DI, launcher, SSE, or broad operation support
  merely to make the two selected calls work.

These are design contradictions, not invitations to hide the gap behind a fallback.

## Documentation and Decision Impact

This design changes sealed architecture and sequencing claims and therefore follows the
repository deviation protocol before implementation:

- Update the generator architecture's ingestion policy from exhaustive reader-DOM whitelist
  surveillance to semantic-risk fail-closed validation.
- Update ADR-0003 in place to describe the current ingestion policy while retaining the own
  generator, Microsoft.OpenApi, SpecIR, Roslyn emission, and tooling-package decisions.
- Update the implementation slice map so the first callable SDK phase is represented by two
  independently mergeable production arcs and `generate` becomes real in the compiler arc
  rather than in the old Slice 3.
- Rewrite the active Slice 1 plan's remaining Tasks 7–11 around lean closeout; completed-task
  history remains in git and the SDD ledger.
- Update the public API design to pin the first callable member inventory, clarify that
  `Pipeline.ExecuteAsync` is the existing behavior core rather than a new extensibility
  framework, and distinguish selection-scoped pre-release curation from the full release gate.
- Stage the testing architecture's level-2 rule: complete contract breadth over selected public
  operations in the callable arc, restoring both-surface breadth when the full profile lands.
- Update ADR-0008 as needed to distinguish pending build-out operations from product exclusions
  and hand-wired operations; its final all-operation and fingerprint commitments remain.
- Update ROADMAP, the slice issue graph, and `blocked_by` edges to the revised arc meanings.
- Preserve the complexity checkpoint and both spike findings in research documentation without
  making temporary workspace artifacts permanent dependencies.

No implementation resumes until these documents agree and the maintainer approves the written
design and revised execution plan.

## Sources

- `docs/research/13-generator-complexity-checkpoint.md`
- `docs/research/08-codegen-spike.md`
- `docs/adr/0003-model-layer-codegen.md`
- `docs/adr/0004-generated-model-principles.md`
- `docs/adr/0005-both-api-surfaces.md`
- `docs/adr/0007-error-model.md`
- `docs/adr/0008-generated-operation-methods.md`
- `docs/adr/0009-unknown-variant-tolerance.md`
- `docs/superpowers/specs/2026-08-09-generator-architecture.md`
- `docs/superpowers/specs/2026-08-09-public-api-design.md`
- `docs/superpowers/specs/2026-08-10-testing-architecture-design.md`
- `docs/superpowers/plans/2026-08-13-m1-arc-b-callable-client.md`
- `spec/openapi.json`
