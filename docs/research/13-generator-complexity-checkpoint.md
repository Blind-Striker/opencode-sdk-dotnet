# Generator complexity checkpoint: findings and delivery decision

Date: 2026-08-11

This document records the completed checkpoint raised after Slice 1 Task 7. The concern was
that ingestion and SpecIR complexity had grown substantially before the repository produced any
SDK source. It captures the evidence gathered against the pinned spec, the reassessment of local
open-source generators, and the delivery direction selected from that evidence.

Current execution state belongs in `docs/ROADMAP.md` and the active SDD ledger. This document is
the dated question -> finding -> decision record.

## Q1: Was the complexity concern grounded?

### How researched

The checkpoint measured the branch at commit
`19349300e5133c6e629a23c60b7a1898dac80ff4`, inspected the generator production and test trees,
read the still-stubbed `generate` command, and compared the repository with upstream's published
JavaScript SDK build path.

### Found

- Since the Slice 1 preparation baseline (`c76a09b^..1934930`), the branch contained 136 changed
  files and +8141 / -370 lines.
- `tools/OpenCode.Sdk.Tools/Generator/` contained 70 files and about 2869 non-blank production
  lines, almost entirely under ingestion. Binder, EmitPlan, emitters, Writer, and generated SDK
  output did not yet exist.
- Two late projection commits accounted for +4757 / -122 lines:
  `c0d098d` added object/literal/union schema projection and `1934930` added per-host walls and
  operation projection.
- Task 7 implementation existed at `1934930`, but its independent review and controller gate had
  not run. The later absorption probes deliberately used that exact production tree without
  changing it.
- Upstream's `packages/sdk/js/script/build.ts` is 119 handwritten lines, but it is not an
  OpenAPI-to-TypeScript generator. It delegates parsing, IR, and emission to
  `@hey-api/openapi-ts`, performs pre-generation document surgery, then applies assertion-guarded
  post-generation patches.
- Upstream's small script and this repository's ingestion tree are therefore not equivalent
  units. This repository had chosen to internalize work that upstream delegates in exchange for
  immutable models, unknown-variant carriers, one AOT serializer registry, both API surfaces,
  and exact public-shape control.

### Decision

The concern was valid. The upstream comparison did not prove that an own generator was wrong,
but the absence of user-visible SDK output made every assurance mechanism justify its cost. No
further horizontal architecture was allowed to land until real-spec absorption and an
off-the-shelf runtime check supplied evidence.

## Q2: Is the repository rebuilding OpenAPI parsing?

### How researched

The Microsoft.OpenApi boundary, current projection code, prior reader probes, and the pinned
document's actual constructs were separated into syntax responsibilities and product-semantic
responsibilities.

### Found

Microsoft.OpenApi 3.9.0 owns:

- JSON and OpenAPI container parsing into a typed DOM.
- Reference construction and resolution.
- Retention of unknown schema keywords and vendor extensions.
- Reader diagnostics and document loading.

The repository's projection owns decisions that the reader cannot make for this SDK:

| Concern | Why it is not parsing |
|---|---|
| Marked versus structural unions | Determines tagged converters versus structural representation for legal `anyOf` / `oneOf` shapes |
| Effect-tag and name/data error styles | Maps two legal object conventions into one typed error hierarchy |
| Response envelope shapes | Drives named typed response members rather than exposing raw wire wrappers |
| Modern versus legacy surface | Applies ADR-0005 product policy from operation IDs |
| Immutable, nullable-last-resort models | Applies the SDK's public API policy |
| `prefixItems` adapter | Recovers one standard JSON Schema 2020-12 keyword retained by the reader as an unrecognized keyword |
| `x-effect-stream` and `x-websocket` | Interprets opencode-specific protocol semantics |

### Decision

The repository is not maintaining a second JSON/OpenAPI parser. A stable semantic projection is
load-bearing, but this finding does not justify every current collaborator or every exhaustive
reader-DOM assurance wall.

## Q3: Can the current production projection absorb the complete pin?

### How researched

A throwaway net10.0 console harness composed the production internals at commit `1934930` by
hand: `SpecReader`, `ProjectionState`, `SchemaProjector`, `GraphKeyBuilder`, and
`OperationProjector` with its current host policies. It ran against `spec/openapi.json` twice
from fresh loads and compared ordered identities, graph key/kind sequences, histograms, errors,
and semantic landmarks.

The harness did not implement or modify production orchestration. It intentionally excluded the
future raw `$ref` sibling sweep, dangling-reference sweep, and hash persistence. Its raw-pointer
lookup was empty; the real `Config.plugin` tuple still projected because the `prefixItems`
adapter consumes the reader's retained unrecognized keyword.

### Found

| Metric | Result |
|---|---|
| OpenAPI version | 3.1.0 |
| Paths | 162 |
| Named component schemas | 472 |
| DOM operations | 188 |
| Reader errors / crashes | 0 / 0 |
| Projected operations | 188: 61 modern, 127 legacy |
| Missing DOM operation IDs | 0 |
| Missing named schemas | 0 |
| Projection errors / crashes | 0 / 0 |
| Graph nodes | 1501 |
| SSE / WebSocket operations | 4 / 1 |
| Semantic landmarks | 23 passed, 0 failed, 0 blocked |
| Fresh-run comparison | Exact match |

The landmarks demonstrated semantics beyond a DOM echo:

- Both API surfaces, SSE metadata, effect-stream payload retention, wildcard paths, and
  WebSocket detection.
- `SessionDurableEvent` as a 28-branch marked `oneOf`, and `Config.formatter` as a structural
  `anyOf`.
- `Config.plugin` as the one tuple reached through `prefixItems`.
- Effect-tag and name/data error styles.
- Bare, data, data/location, cursor/data, data/hasMore, and no-content envelopes.
- Unrestricted-schema and special-number sites.

The projected pin contained 99 marked `anyOf` unions, 34 structural `anyOf` unions, and one
marked `oneOf`. It also contained 20 Effect-tag errors and 15 name/data errors. These are
generation decisions the Binder would otherwise repeatedly derive from the reader DOM.

### Decision

The absorption result was green: current production projection can represent the complete pin,
deterministically and without silent schema or operation loss. This validates the projection
boundary and its core vocabulary. It does not validate reflection-based host inventories, full
intermediate snapshots, document-wide pre-consumer hashes, or every existing test as the least
costly assurance policy.

## Q4: Can an OSS/local generator replace the custom semantic layer?

### How researched

The earlier real-spec generation runs in `docs/research/08-codegen-spike.md` were rechecked
against current local/pinnable candidates. Kiota then received an additional generated-runtime
wire-fidelity probe using Microsoft.OpenApi.Kiota 1.34.1, Microsoft.Kiota.Bundle 2.0.0, and a
net10.0 recording HTTP handler. Refitter 2.1.3 was inspected from its tagged source because it
adds immutable records and a generated `JsonSerializerContext` on top of NSwag.

### Found

The prior generation results still held:

| Generator | Blocking result on this pin |
|---|---|
| Kiota 1.34.1 | Intersection wrappers require an OpenAPI discriminator that the pin does not have; no System.Text.Json source-generation model |
| NSwag 14.7.1 | Discriminator-free unions collapse or mis-shape; output also had independent compile and OpenAPI 3.1 interpretation failures |
| OpenAPI Generator 7.24.0 | Speculative unions, hundreds of contexts, collisions, reflection fallback, and output that did not compile |
| Refitter 2.1.3 | NSwag 14.7.1 underneath; union rewriting requires `DiscriminatorObject` |

Refitter can emit immutable records and a serializer context, but those features do not discover
opencode's single-value-enum marker convention. Supplying synthetic discriminators would require
a semantic preprocessor and custom unknown-carrier/error/public-surface work, recreating the
boundary the replacement was meant to remove.

Kiota's generation log contained 24 named-schema no-discriminator warnings plus one inline-route
warning, and 91 failures to create declared error types. The harness itself built with zero
warnings and errors; the failures below are generated-runtime behavior, not harness compilation
problems.

| Runtime probe | Result |
|---|---|
| Simple health response | Preserved request and typed values |
| Deliberately selected outbound `TextPart` branch | PATCH path and request JSON preserved |
| Valid inbound `TextPart` | `type` disappeared; identity fields populated `AgentPart`, text populated `ReasoningPart`, and `TextPart` stayed empty |
| Unknown `Part` tag | Tag and unknown raw field disappeared on round trip |
| Valid `SessionDurableEvent` | No branch selected; the complete payload was lost |
| Declared 400 body | Generic `ApiException`; body unavailable because no error factory was generated |
| SSE transport | Bytes preserved exactly, but exposed only as `Stream` |

### Decision

No OSS/local candidate can be adopted as-is without violating the locked wire, unknown-variant,
error, AOT, or public-surface contracts. A delegate-and-patch route would need a semantic
preprocessor and substantial generated-runtime replacement, so it is not a smaller bounded
adaptation. The own Microsoft.OpenApi -> SpecIR -> Binder -> Roslyn path remains selected.

## Q5: Which assurance mechanisms are actually justified?

### Found

The two probes separate correctness machinery from assurance policy:

- Semantic projection is necessary: raw Kiota lost known and unknown union data and typed error
  bodies, while the production projection represented every pinned operation and schema.
- The absorption run included the reflection host walls, so it proves compatibility with the pin,
  not that those walls caused the semantic result or are safe to delete without replacement.
- Reflection host walls enforce an assurance policy. Their consumer/deletion audit must retain
  explicit checks wherever removing one would otherwise permit wrong emitted behavior.
- A full DOM default inventory reports library shape changes whether or not they affect emitted
  behavior.
- A full SpecIR snapshot would duplicate the generated source that consumers actually review.
- Raw hashes for every operation and schema have no current consumer. Fingerprints become useful
  when an operation is deliberately excluded or hand-wired.
- Git diffs cannot reveal silently ignored constructs, so deleting exhaustive walls does not
  justify best-effort generation. Known lossy seams still need explicit validators and landmarks.

### Decision

Generation remains fail-closed by semantic risk. The generator refuses any selected construct
that could change emitted wire or public behavior, while descriptive unused reader members and
unknown non-semantic extensions do not fail merely because they exist.

Drift detection uses the pinned-spec diff and committed generated-source diff as the primary
radar, backed by targeted gates for reader/version diagnostics, `$ref` siblings and dangling
references, `prefixItems`, Microsoft.OpenApi boundary leakage, deterministic graph keys,
union classification, unrestricted schemas, stream metadata, and both API surfaces.

The selected design replaces or defers the generic reflection `HostMemberWhitelist<T>`, complete
DOM/default inventory, full SpecIR snapshot, pre-consumer document-wide hashes, and fatal
treatment of every unknown extension. Each persistent SpecIR fact and validation mechanism must
name an emitter, Binder, runtime, refresh, or standing contract consumer.

## Q6: How should delivery change?

### Found

The original horizontal sequence delayed the first callable client until after broad ingestion,
binding, model emission, transport, and operation work. Even if each layer were correct, that
sequence amplified integration and review risk because no production SDK call exercised the
complete seam early.

`v2.health.get` supplies the minimal control path. `v2.session.message` supplies a representative
hard path with bound-client placement, two path parameters, a data envelope, nested marked
unions, and declared typed errors. Together they exercise the final architecture without
requiring retry, telemetry, DI, launcher, or streams.

### Decision

Slice 1 closes as a lean ingestion/SpecIR module. The first callable SDK phase then lands as two
independently mergeable production arcs:

1. Selected compiler and committed models for exactly `v2.health.get` and
   `v2.session.message`.
2. Callable generated clients over the minimal hand-written transport, response, request-option,
   and typed-error core.

Generated output is committed under `src/OpenCode.Sdk`. Pending operations are reported as
build-out state, not product exclusions, and a machine-owned marker makes `dotnet pack` fail while
the profile is partial. Later breadth proceeds in bounded vertical operation batches, each with
its reachable models, operation methods, and contract tests; there is no separate orphan-model
horizontal milestone.

The selected delivery status and milestone boundary live in `docs/ROADMAP.md`; this checkpoint
retains the supporting verification evidence and complexity decision.

## Sources

### Repository sources

- `spec/openapi.json` - pinned OpenAPI artifact used by both probes.
- `external/opencode/packages/sdk/js/script/build.ts` - upstream delegate-and-patch build script.
- `external/opencode/packages/sdk/js/package.json` - pinned `@hey-api/openapi-ts` dependency.
- `external/opencode/packages/sdk/js/src/v2/client.ts` - generated client consumption and thin
  handwritten wrapper.
- `docs/research/08-codegen-spike.md` - Kiota, NSwag, OpenAPI Generator, and own-emitter run
  evidence.
- `docs/research/00-research-log.md` Q56-Q59 and Q61 - Microsoft.OpenApi boundary and
  complexity-grill evidence.
- `docs/adr/0003-model-layer-codegen.md` - own generator, Microsoft.OpenApi reader, semantic
  projection, and Roslyn emission.
- `docs/ROADMAP.md` - selected delivery status and milestone boundary.
- Commit `1934930` - exact production ingestion implementation used by the absorption probe.

### External primary sources

- [Refitter 2.1.3 release](https://github.com/christianhelle/refitter/releases/tag/2.1.3).
- [Refitter 2.1.3 core project][refitter-core] - pins NSwag 14.7.1.
- [Refitter 2.1.3 discriminator mutator][refitter-mutator] - exits when
  `DiscriminatorObject` is absent.
- [Refitter 2.1.3 serializer-context polymorphism test][refitter-context] - confirms the context
  feature against an explicit OpenAPI discriminator.

[refitter-core]: https://github.com/christianhelle/refitter/blob/2.1.3/src/Refitter.Core/Refitter.Core.csproj
[refitter-mutator]: https://github.com/christianhelle/refitter/blob/2.1.3/src/Refitter.Core/Mutators/OneOfDiscriminatorToAllOfMutator.cs
[refitter-context]: https://github.com/christianhelle/refitter/blob/2.1.3/src/Refitter.Tests/Scenarios/GenerateJsonSerializerContextPolymorphismTests.cs
