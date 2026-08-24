# Generator Binding Locality — architecture scan and M5-gate reassessment method

Date: 2026-08-24

Dated evidence from the Session 40 architecture scan (research log Q126), preserved in detail so the
pre-M5 decision on the B-track candidates does not depend on session memory. Scan baseline: source at
`1222664`; the binding slice was untouched between that commit and `05cd5d7`. This document is
evidence, not policy — `docs/ROADMAP.md` owns the open item and its reassessment moment; nothing here
is scheduled work.

## 1. Scan method

Read-only depth scan of `tools/` and `tests/OpenCode.Sdk.Tools.Tests/` using the shared design
vocabulary (module, interface, depth, seam, adapter, leverage, locality; deep = small interface over
large behavior, overloaded-deep = small interface over too many entangled behaviors), with:

1. **Hot-spot analysis** — file-change frequency over the last 80 commits
   (`git log --name-only --pretty=format: -80 | sort | uniq -c | sort -rn`).
2. **Module map** — per module: seam location, interface size including non-obvious caller
   obligations, implementation size, depth verdict.
3. **Deletion tests** on suspected shallow modules (does deleting concentrate or merely move
   complexity?).
4. **Friction traces** — commit archaeology of one construct-sized and one operation-shape-sized
   feature to count the files and regions each actually touched.
5. **Seam count check** — one adapter = hypothetical seam, two = real.

Hot-spot result: `Generator/Binding/OperationPlanBinder.cs` changed 20× (its test file 20×),
`SchemaPlanBinder.cs` 14×, `SpecBinder.cs` 9×, `CurationValidator.cs` 8×, and test support
`EmitterPlanFixture.cs` 18×, against 8–9× for the busiest emitters. Binding is the tool's hot spot.

## 2. Stage map and overall verdict

Four stages, four narrow inter-stage seams, no circular dependencies, one composition root
(`ToolApp`, 20 DI registrations):

| Stage | Entry | Crossing type | Mass |
|---|---|---|---|
| Ingestion | `ISpecIngestion.IngestAsync` | `SpecDocument` (operations + `SchemaNode` graph, 20 record kinds) | ~1,900 lines behind 1 method — the tool's deepest module |
| Config | loaders | `OperationSelection`, `GenerationCuration` | small |
| Binding | `ISpecBinder.Bind` | `EmitPlan` (~40 plan records) | **~4,000 lines behind 2 concrete classes** |
| Emission | `SourceEmitter.Emit` | `GeneratedSource[]` | 11 emitters, uniform `Emit(plan-slice)`, one file per output family |
| Output | `IGenerationWriter.WriteAsync` | `WriteResult` | 391 lines, 8 named safety walls — most self-contained module |

Verdict: healthy skeleton, one overloaded organ (Binding), one test fixture paying that organ's bill.
Emission's churn is proportional (a feature that changes emitted C# must change an emitter) — correct
locality, not friction. Zero mocks anywhere in the tool tests; substitution happens only at the two
seams with real second adapters (`IFileSystem` via Testably, `IProjectFormatter` via a recorder).
Three DI abstractions have exactly one adapter and no test consumer (`ISpecIngestion`, `ISpecBinder`,
`IGenerationWriter`) — hypothetical seams, noted, not worth churn to remove.

## 3. `OperationPlanBinder` — overloaded-deep, 1,319 lines, 24 responsibilities

Outer class (≈300 lines): selected-operation iteration; client-family assembly (root/collection/handle
tri-split, curated group merging); handle-parameter derivation; per-client member-collision checking;
global type-name collision checking across six name spaces — plus three static string tables:
`ReservedSpineTypeNames` (16), `ReservedParameterNames` (4), `ReservedPayloadNames` (16). The tables
mirror facts about the hand-written runtime and emitted C# with no compile-time link, so **adding a
public spine type in `src/` is silently a `tools/` edit**; a missed entry emits a CS0101 twin.

Inner `SingleOperationBinder` (≈1,020 lines, 19 responsibilities): curation lookup; HTTP wire-shape
wall; path-parameter wall; status-shape wall (exactly-one-success, 200/204 only, no 1xx/3xx — the
fail-closed wall that makes A3's multi-success case self-announcing); SSE frame-contract binding;
`x-effect-stream` validation; JSON-string frame unwrapping; envelope binding across five shapes (six
`Bind*Payload` methods, ~250 lines); payload-name derivation; location-sibling binding; structural
recognizers of runtime spine shapes (`IsLocationSelectorShape`, `IsListCursorShape`,
`IsParentFilterShape`); error-map binding; path-parameter plan binding; query-request binding with
deepObject location channel and five query-value kinds; `ListRequest` profile matching; request-body
binding including a cross-module name-agreement check against `SchemaNameResolver` (the naming
decision has two owners and a runtime error reconciles them); body+query merge policy; pagination
binding; a third private copy of ref-graph `Resolve`; five refusal helpers existing only for
return-type inference.

**Why it churns — the locality pattern:** `OperationPlan` carries five optional slots
(`QueryRequest`, `RequestBody`, `Envelope`, `Stream`, `Pagination`). Every new operation-shaped
feature must edit four scattered regions of this one file: (a) a new nullable slot on
`OperationPlan`, (b) a new `Bind*` method, (c) the assembly block and its composite null-check,
(d) a `CheckTypeNameCollisions` entry. Nothing is addable as a new file; the file changes on every
feature while no single region does.

## 4. `SchemaPlanBinder` — same shape at half scale, 593 lines, 8 responsibilities

Two independent union systems share the file: marked-union binding (branch resolution,
uniform-nested-marker detection, marker-spanning expansion, inhabitation → known-impossible tags —
~294 lines, 49% of the file) and error-union synthesis (`BindErrorUnion`, ~83 lines — synthesizes
`IOpenCodeError` from an `ErrorStyle` closure scan; the union exists nowhere in the spec). They share
only a mutable `Dictionary<string, List<string>>` inheritance accumulator threaded through five
methods, so every union-shaped feature (ADR-0011 interfaces, ADR-0015 impossible tags, typed stream
causes) touches both — 14 changes. Object/enum binding, structural-union delegation, and registry
composition fill the rest. The neighboring extractions (`TypePlanBinder`,
`StructuralUnionPlanBinder`, `UnionMembershipValidator`, `SchemaInhabitationPolicy`) are the
pattern's success stories — deep, one method, 51–203 lines each.

## 5. Friction traces (commit archaeology)

- **`28d09e1`** (typed stream failure causes — one construct): 38 files, +1,516/−439; 28 production
  files across all four layers, including `OperationPlanBinder` +133, `SchemaPlanBinder` +122,
  `UnionEmitter` +149, and a near-rewrite of `OperationPlanBinderTests` (+638). Vertical spread
  across layers is the design and a fair price; the avoidable cost concentrates in Binding.
- **`ec043f4`** (cursor pagination — one operation shape): 12 files; `OperationPlanBinder` +86
  across the four regions, one new slot, one new emitter, `ResponseAdapterEmitter` +112,
  `EmitterPlanFixture` +7.

## 6. Test infrastructure findings

`EmitterPlanFixture` (814 lines, 18 changes) has two modes: ~795 lines hand-build an `EmitPlan` in a
fictional domain consumed by 13 emitter test suites (every plan-record field addition edits it — the
churn list maps 1:1 onto feature commits), while two 8-line methods derive real plans from scenario
specs through the real ingest-and-bind path — the healthy pattern already present. Binder tests are
honest: 92 scenario-built specs plus 6 pinned-spec goldens in `OperationPlanBinderTests` (1,952
lines), all asserting through `BindingException.Errors`; the cost is that every binder test is an
integration test over Ingestion+Binding — a deliberate trade keeping tests honest about the pinned
dialect. `BindingTestHost` restates the eight-object `SpecBinder` graph by hand (no
`CreateDefault()` factory) — mild.

## 7. Candidates (as scanned; re-verify shapes at the gate)

- **B1 — facet binders:** keep `OperationPlanBinder`'s seam and errors; give each optional plan slot
  its own facet binder over a shared read-only `OperationFacetContext` (doc, operation, curation,
  names, resolve); spine-shape recognizers move to a `SpineShapePolicy`. A new operation shape
  becomes one facet file plus one assembly line. Sketch: binder ~330 + context ~60 + five facets
  55–280 each + wire-shape wall ~110.
- **B2 — one reserved-name owner:** a `ReservedNamePolicy` owning all three tables; the emitters that
  append reserved parameters and declare envelope spine members read the same sets; one reflection
  test asserts the spine list covers the `OpenCode.Sdk` public surface, turning the silent CS0101
  into a red test. Smallest candidate, highest safety yield; can ride any generator-touching
  increment opportunistically (Increment 2b of the runtime plan is the first such contact).
- **B3 — scenario-derived fixture:** one `EmitterCoverageScenario` spec exercising every emitter;
  `EmitterPlanFixture.Create()` derives from it through ingest-and-bind; hand-built residue only for
  shapes the pinned dialect cannot express. Call sites unchanged.
- **B4 — extract error-union synthesis:** `ErrorUnionBinder` beside the existing extractions; a small
  `UnionMembershipMap` replaces the raw shared dictionary. Sequence after B1.

## 8. Reassessment method at the M5 gate

Before the first M5 breadth batch (and earlier if Increment 2b's binder work makes the pain acute):

1. **Drift check** — `git log --oneline 05cd5d7.. -- tools/OpenCode.Sdk.Tools/Generator/Binding`
   plus the hot-spot frequency command from §1 over the commits since; re-verify §3's four-region
   pattern and the three tables against current line numbers (2b adds status-table emission and may
   have reshaped adapter emitters).
2. **Cost model** — from the next batch's operation list, count which operations introduce a new
   shape versus reuse existing slots; B1's payoff scales with new-shape count, so a batch of
   shape-reusing operations does not justify pulling B1 forward.
3. **Decide per candidate** — B2 independently (near-free, safety-motivated); B1 before the first
   new-shape batch or not at all this milestone; B3 only alongside or after B1 (it mirrors the same
   slot growth); B4 only after B1.
4. **Method on re-scan** — repeat §1's five steps scoped to Binding; the vocabulary and deletion
   tests are the stable instrument, this document is the baseline to diff against.

Decision authority stays with the maintainer at that gate; this document only guarantees the
evidence and the instrument survive until then.
