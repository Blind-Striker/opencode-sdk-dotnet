# Codegen spike: the model-layer mechanism

Date: 2026-08-08

> Dated evidence and decision history, not current policy. Follow current canon through
> `AGENTS.md`.
>
> Spike record (queue item 1) and primary evidence for the grill session. Question: which
> mechanism keeps the model layer mechanical — our own generator, or Kiota / NSwag / OpenAPI
> Generator? For the own-generator route: which emission mechanism (Roslyn syntax trees vs
> template/string) and which packaging (standalone CLI vs Roslyn incremental source generator)?
> Method: primary-source tool survey, then all three tools run against the real
> `spec/openapi.json` (v1.18.15) with a strict-analyzer compile harness replicating the repo
> regime, an own-generator slice implemented twice (both emission mechanisms) on the same spec,
> and a specialist analysis of the packaging axis. Prototypes were throwaway (scratchpad);
> everything decision-relevant is recorded here.

## Decisions

1. **Model-layer mechanism: our own generator.** All three off-the-shelf tools fail the
   discriminated-union criterion structurally on this spec, and none can emit the locked
   serialization design (single source-generated `JsonSerializerContext` registry). Kiota,
   NSwag, and OpenAPI Generator are eliminated on run evidence, not taste.
2. **Emission mechanism: Roslyn syntax trees** (maintainer decision). The slice priced the
   trade precisely — template/string emission is cheaper at prototype scale (no dependency,
   ~4× faster, exact formatting control; evidence below) — but the full generator emits far
   more than flat models (request/response wrappers, converters, possibly Result-shaped
   methods), and semantic construction is judged more maintainable at that scale. The
   measured costs are accepted with mitigations: formatting is owned by a `dotnet format`
   post-step in the tool, and doc-comment/directive trivia is emitted as parsed strings
   (standard Roslyn practice).
3. **Packaging: repo tooling under `tools/`** — emission engine as a library behind a thin
   file-based `.cs` entry (committed with the executable bit), bound to the repo build rules.
   Generated output is committed into the SDK project and CI regen-verifies; the same tool
   owns spec refresh (submodule pin bump, `spec/` copy, `SNAPSHOT.md` stamp). The
   incremental-source-generator shape is structurally blocked: a compile-time-emitted
   `[JsonSerializable]` registry is invisible to the downstream System.Text.Json source
   generator (Roslyn generators never see each other's output), which would silently break
   the locked AOT commitment.

Reversal triggers (for the eventual ADR): revisit packaging if Roslyn ships generator
chaining, if the spec becomes a live per-commit input, or if the generator becomes a shipped
product for third-party specs.

## The spec's actual dialect (the fact that decides everything)

Counted over `spec/openapi.json` (162 paths, 472 schemas, 188 operations / 61 `v2.*`):

| Construct | Count | Meaning |
|---|---|---|
| `anyOf` | 172 | The union mechanism (`Part` 12 variants, `Event` 89, `V2Event` 88, `ToolState`, `Auth`, …) |
| `oneOf` | 1 | Only `SessionDurableEvent` |
| `allOf` / `discriminator` | 0 / 0 | **No OpenAPI-style polymorphism anywhere** |
| Single-value `enum` markers | 513 | The de-facto discriminator: every variant self-identifies via a required literal (`"type": {"enum": ["text"]}`, `"status": {"enum": ["running"]}`) |
| `type` arrays / `const` / `$defs` | 0 | The hard OpenAPI 3.1 constructs are absent; nullability is an `anyOf` branch (8×) |
| `additionalProperties: false` | 1229 | Strict closed schemas |

So criterion 1 (3.1 support) matters far less than assumed, and criterion 2 (unions) is
decisive: every tool's union handling presupposes the `discriminator` keyword; the spec's
convention is invisible to all of them, while System.Text.Json's name-based polymorphism
(`[JsonPolymorphic]` + `[JsonDerivedType]`) matches it exactly.

Upstream corroborates: its published JS SDK fights `@hey-api/openapi-ts` with pre-generation
document surgery and guarded post-generation regex patches, and its next-gen client comes from
a hand-rolled ~1.2k-line generator (`packages/httpapi-codegen`) with caller-side endpoint
filtering and committed, CI-regen-verified output — structurally the pattern chosen here.

## Verdict matrix (run evidence, 2026-08-08)

K1 OpenAPI 3.1 · K2 unions → C# · K3 `JsonSerializerContext` · K4 strict analyzers
(`AnalysisMode=All` + TWAE + the repo's 7 analyzer packages) · K5 v2-only filtering ·
K6 return-shape flexibility.

| | Kiota 1.34.1 | NSwag 14.7.1 | OpenAPI Gen 7.24.0 (generichost) | Own slice |
|---|---|---|---|---|
| K1 | pass (native) | **silent 3.0 misparse** — 3.1 doc accepted, byte-identical to a 3.0-labeled run; `anyOf`-null nullability dropped | pass with `--skip-validate-spec` | pass (plain JSON read) |
| K2 | **fail** — intersection wrappers populate all variants at once; 24 generator warnings predict serialization errors; the one `oneOf` dispatches on schema names that never appear in payloads | **fail** — `Part`/`V2Event` collapse to empty classes with a `[JsonExtensionData]` bag | **fail** — wrapper with one nullable property per variant; converter speculatively parses all 88 `V2Event` variants per event | **pass** — functional dispatch proven, nested unions included |
| K3 | **fail by design** — format-agnostic `IParsable`, zero STJ attributes, 6 `Microsoft.Kiota.*` runtime packages | **fail** — no context emission exists | **fail on this spec** — 893 per-model contexts, SYSLIB1031 ×416 name collisions, reflection fallback in the resolver chain | **pass** — single registry emitted |
| K4 | pass 0/0 via `<auto-generated/>` + self-emitted pragmas | **fail** — output doesn't compile (duplicate partial clients; `Data16` referenced but never generated; assembly-level CA1708 `OAuth`/`Oauth` collision immune to generated-code exemption) | split — clean subset passes 0/0 via header, but 35 files have syntax errors (HTML-escaped generics leaked into source) | pass 0/0 via `.g.cs` convention; on-merit gap quantified below |
| K5 | path globs only — `/api/**` keeps 58 ops, **misses the 3 `v2.*` ops under `/experimental/`** | operationId include list exists but exact-match only | `FILTER=operationId:` exact-match OR-list (automatable); `path:` prefix misses the same 3 | trivial — closure walk keyed on the `v2.` operationId prefix |
| K6 | none (compiled writers, no templates) | full Liquid fork | best of the three (`IOk<T>`-style status shapes; mustache override) | whatever we emit |

Cross-cutting disqualifiers found only by running: NSwag's inline-schema naming renumbers
neighbors on any spec refresh (`Data1`…`Data135`, `Body2`…) — unreviewable regen diffs;
OpenAPI Generator reproduced five independent emission-bug classes on this one spec and never
produced a compiling model layer in any configuration.

## The own-generator slice

Built twice on a shared ~190-line parser/IR — template emitter (~110 lines) and Roslyn
emitter (~120 lines, Microsoft.CodeAnalysis.CSharp 5.6) — over the `Part` transitive closure
(3 unions incl. nested `ToolState`, 35 objects, promoted inline types): 39 files each,
~1000 LOC output, 68 ms vs 261 ms.

- **Dispatch works.** Emitted `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` +
  `[JsonDerivedType(typeof(TextPart), "text")]` deserializes real payload shapes to the right
  variants, including the nested `status`-keyed union, with
  `AllowOutOfOrderMetadataProperties = true` covering the spec's non-leading discriminator
  position. Round-trip re-emits the discriminator. Unknown discriminator throws
  `JsonException` — the forward-compatibility strategy is an API-design-session question.
- **Strict analyzers:** both outputs compile 0 warnings / 0 errors under the exact repo
  regime. The `.g.cs` name alone triggers generated-code treatment (no header needed) — which
  also means project-level `<Nullable>` stops applying and each file must carry its own
  `#nullable enable` (CS8669 otherwise).
- **On-merit probe** (same files renamed `.cs`, full analyzer wall engaged): 186 diagnostics,
  ~91% two mechanical style rules (IDE0065 using placement ×92, IDE0240 redundant `#nullable`
  ×78) plus three genuine design choices (CA1056 `Uri` vs `string` ×8, CA1707 underscores ×4,
  S101 acronym casing ×4). Every family is mechanically fixable in our emitter; none is
  fixable in an off-the-shelf tool. Full on-merit conformance is therefore attainable if the
  grill/API sessions want it; generated-code exemption is the zero-cost default.
- **Emission comparison (slice evidence):** identical output; the Roslyn variant writes XML
  docs and directives via parsed strings anyway (structured doc-trivia factories are
  impractical), pays 4× runtime, adds a dependency, and cedes formatting to
  `NormalizeWhitespace` — i.e. template emission is cheaper at slice scale. The maintainer
  weighed this against full-generator scale, where semantic construction (type-safe
  composition, refactorability across many emitted shapes) dominates maintenance cost, and
  chose Roslyn (decision 2); formatting is reclaimed by the tool's `dotnet format` post-step.

## Corrections to prior assumptions

- **"v2 = `/api/*`" is not exact:** 3 of 61 `v2.*` operations live under
  `/experimental/project/{projectID}/copy*`. Filtering must key on the operationId prefix,
  not the path.
- **`Part` is legacy-only:** no `/api` route references it; the v2 surface's unions are
  `V2Event` (88 variants, SSE) and friends — same literal convention, so the slice evidence
  transfers.
- Survey correction: NSwag does have `IncludedOperationIds`/`ExcludedOperationIds`
  (exact-match), contrary to the initial doc survey.

## Feed-forward

- **Grill session:** decisions 1–3 above + reversal triggers are ADR candidates (hybrid
  codegen ADR can now name mechanism, emission, and packaging concretely).
- **API design session:** unknown-discriminator/forward-compat strategy for `V2Event` SSE;
  `Uri` vs `string` for URL properties; acronym casing policy (`APIError` vs `ApiError`);
  identifier mapping for underscore JSON names; `WhenWritingNull` (the slice writes explicit
  nulls); on-merit style conformance vs generated-code exemption for the emitted layer.
- **Generator build-out (implementation phase):** the generator is scope-agnostic — the
  surface filter (legacy vs `v2.*`) is a parameter (Part closure was the union stress test);
  remaining constructs to cover: real enums (104), the single `oneOf`, request/response
  wrapper types, multi-TFM downlevel emission, `x-effect-stream` SSE item schemas (invisible
  to every off-the-shelf tool).
