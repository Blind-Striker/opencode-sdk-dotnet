# Generator Architecture — the model-layer generator and repo tooling

Date: 2026-08-09

Design specification produced by the generator-architecture brainstorm session (2026-08-09,
ROADMAP queue item 1). Every decision below was discussed and sealed individually with the
maintainer. This spec feeds a fresh-context grill session next; claims are therefore tied to
their evidence inline. Numeric claims marked **[verified]** were counted by script against
`spec/openapi.json` (v1.18.15) during this session; upstream-code claims cite exact files in
the `external/opencode` submodule read this session.

## 1. Scope and inputs

**In scope:** the internal architecture of the model-layer generator and the repo tooling that
hosts it — tooling layout and command surface, pipeline stages (parser/IR, binder, emitters,
writer), curation-config format and semantics, fail-closed drift mechanics, file mechanics for
on-merit generated output, multi-TFM emission, the two committed manifests (output +
fingerprint), spec-refresh tooling, and CI wiring.

**Out of scope:** the public API the generator emits (locked by the public API design spec,
`2026-08-09-public-api-design.md`); the testing architecture (its own queued session — §11);
launcher/transport/SSE internals (hand-written core, ADR-0008).

**Contract this design implements** (public API spec §1, restated): inputs are the pinned spec
plus a declarative fail-closed curation config; outputs are models, response envelopes, request
input records, operation methods for both surfaces, per-union tolerant converters (ADR-0009),
forward-only paginators, the single `[JsonSerializable]` registry, `OpenCodeRoutes`, the
fingerprint manifest (ADR-0008), emitted guard clauses, and emitted XML docs with
`<exception cref>` lists. Output passes the analyzer wall on merit (ADR-0003); a `dotnet
format` post-step and CI regen-verify guard it.

**Evidence base:** ADRs 0003/0004/0005/0008/0009; research docs 08 (codegen spike — parser/IR
boundary, emission trade, packaging), 09/10 (dialect drift, genealogy); the public API spec
(§5.1, §8, §11.2, §12, §14, §15); upstream's own generator
`external/opencode/packages/httpapi-codegen/src/index.ts` (1185 lines, read in full this
session); the PathSmith repository (`E:\repos\my-projects\env-variable-tools`) as the tools
structure reference; scripted verification against the pinned spec (results inline below).

## 2. Principles

1. **Policy in code, names and exceptions in data.** Everything mechanically derivable from
   the spec is a hardcoded generator rule (PascalCase + `[JsonPropertyName]` mapping,
   `required`/nullability derivation, union analysis, converters, guards, XML docs, verb
   inference). Everything not derivable — public names, exclusions, semantic overrides — lives
   in the curation config, and only there.
2. **Fail-closed at build time, tolerant at runtime** (public API spec §2.1 relay). The
   generator breaks loudly on anything unmapped, unknown, or drifted; the *emitted* code is
   deliberately tolerant (unknown-variant carriers, ADR-0009). The two regimes are opposite by
   design.
3. **`tools/` is the repo's centralized tooling home** (maintainer principle, sealed this
   session). The generator is its first resident, not its identity: future CI/CD or
   local-development tooling lands in the same project as sibling command/namespace areas.
4. **Determinism is a hard requirement.** Same spec + same curation ⇒ byte-identical output:
   stable file and member ordering, culture-invariant string operations, LF line endings.
   CI regen-verify is built on this.
5. **Batched failure reporting.** Coverage violations are collected and reported together with
   category and subject — one fix-up PR per drift event, not a break/fix/break loop.

## 3. Tooling architecture

### 3.1 Layout (sealed)

```
tools/
├── opencode-tool.cs                  ← file-based entry (shebang, committed executable bit)
│     #!/usr/bin/env dotnet
│     #:project ./OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj
│     return await OpenCode.Sdk.Tools.ToolApp.RunAsync(args);
├── curation.json                     ← curation config (§5)
└── OpenCode.Sdk.Tools/               ← csproj library, bound to the repo build rules
    ├── ToolApp.cs                    ← CommandApp + DI composition (testable factory)
    ├── Commands/                     ← CLI surface for all tooling areas
    │   ├── GenerateCommand.cs        ←   generate [--verify] [--update-fingerprints]
    │   └── RefreshSpecCommand.cs     ←   refresh-spec --ref <tag|commit>
    ├── Generator/
    │   ├── Parsing/                  ← Parser + SpecIR types (§4.1)
    │   ├── Binding/                  ← Binder + curation models + EmitPlan types (§4.2)
    │   ├── Emission/                 ← per-artifact-family emitters (§6)
    │   └── Output/                   ← Writer: output manifest, stale cleanup, format (§8)
    └── Infrastructure/               ← shared: IFileSystem, git/process wrappers

tests/OpenCode.Sdk.Tools.Tests/       ← TUnit + Spectre.Console.Cli.Testing + TestingHelpers
```

This resolves ADR-0003's "emission library behind a thin file-based entry" against the
PathSmith reference structure by synthesis: the library carries the full PathSmith-style
composition (DI via `DependencyInjectionRegistrar`, command classes, service abstractions —
PathSmith `Program.cs` + `Commands/` + `Services/` pattern, read this session), but the
`CommandApp` wiring lives in a `ToolApp` factory so `Spectre.Console.Cli.Testing`'s
`CommandAppTester` can exercise it; the file-based entry shrinks to three lines and merely
delegates. ADR-0003's packaging clause survives unchanged.

Deviations from PathSmith, both deliberate: `ToolApp` factory instead of logic in
`Program.cs` (testability of the wiring); pipeline-stage folders under `Generator/` instead of
a flat `Services/` (MA0048/IDE0130 make file=type and folder=namespace mandatory anyway, and
the generator is one tooling area among future siblings — §2 principle 3).

**Stack** (ROADMAP queue 2, confirmed): Spectre.Console.Cli (+ the DI registrar + Testing
packages), CliWrap for git and `dotnet format` invocations (the no-CliWrap rule is
SDK-product-scoped, ADR-0001), the TestableIO trio for all filesystem I/O (public API spec
§3), System.Text.Json for spec and curation parsing, Microsoft.CodeAnalysis.CSharp for
emission (ADR-0003). All versions enter `Directory.Packages.props`.

### 3.2 Command surface

- **`generate`** — parse → bind → emit → write. Fingerprint verification (§9) runs inside
  every invocation, not as a separate command.
  - `--verify` — CI/regen-verify mode: full in-place regeneration (including the format
    post-step), then `git status --porcelain` over the generated paths; dirty ⇒ nonzero exit
    plus the file list. In-memory comparison was considered and rejected: the `dotnet format`
    post-step requires an MSBuild workspace, so a memory-side compare would diff unformatted
    output against formatted commits and always report drift. In-place regen + git-clean check
    is the ecosystem's regen-verify shape (upstream commits and regen-verifies its generated
    client the same way — research doc 08).
  - `--update-fingerprints` — the explicit human review gate for fingerprint drift (§9).
- **`refresh-spec --ref <tag|commit>`** — the spec-refresh workflow (§10).

### 3.3 UNVERIFIED — carried to build-out

The file-based entry's interaction with the repo's strict `Directory.Build.props` and central
package management is unverified (the entry uses only `#:project`, no `#:package`, which
avoids the known CPM/version friction — but this must be proven, not assumed). Verification is
the first build-out step; the recorded fallback is dropping the entry and promoting the
library to a console app (a one-line ADR-0003 correction, maintainer-approved), with `ToolApp`
unchanged.

## 4. Pipeline architecture (sealed: two-stage)

```
spec/openapi.json ─▶ Parser ─▶ SpecIR ─▶ Binder ─▶ EmitPlan ─▶ Emitters ─▶ Writer
                              (wire-      │ + curation.json     (Roslyn)     │ output manifest
                               faithful)  │ all fail-closed                  │ stale cleanup
                                          ▼ checks, batched                  ▼ dotnet format
                                       batch error report               src/OpenCode.Sdk/
```

The alternative — a single stage applying curation during parse, the spike's ~190-line shape
scaled up — was considered and rejected: curation errors would surface one at a time
mid-parse, wire analysis and C# naming would interleave, and the parser/emitter reversal
boundary that ADR-0003 records ("if Roslyn emission proves a net burden, the emitter half is
swapped without touching spec parsing") would blur. The two-stage shape is also upstream's
own: `httpapi-codegen` separates `compile()` (producing a `Contract` IR of groups, endpoints,
and operations) from `emitPromise`/`emitEffect`/`write` over that IR
(`packages/httpapi-codegen/src/index.ts`).

### 4.1 Parser and SpecIR

SpecIR is wire-faithful and contains zero C# concepts. Inventory:

- **`SpecOperation`** — operationId; surface (`Modern`/`Legacy`, keyed on the `v2.`
  operationId prefix, never the path: 3 of 61 modern ops live under
  `/experimental/project/{projectID}/copy*` **[verified]**); dotted group segments; HTTP
  method; path template with a wildcard flag (`/api/fs/read/*` is the only wildcard path
  **[verified]**); parameters (path/query/header, wire type, required); request body;
  responses (status → content type + schema; 204 = no-content); SSE flag (detected by the
  `text/event-stream` response content type — 4 ops: `global.event`, `event.subscribe`,
  `v2.session.events`, `v2.event.subscribe` **[verified]**; `x-effect-stream` appears only on
  `v2.session.events` **[verified]** and is therefore not the detector); declared error
  responses.
- **Schema graph** — named schemas plus promoted inline types (the spike's promotion
  pattern, doc 08). Node kinds: object (properties, required set, `additionalProperties`),
  union (`anyOf` + literal-marker analysis, including nested unions such as `ToolState`),
  enum (104 multi-value enums **[verified]**), primitive/array/dictionary, nullable (the
  `anyOf`-null branch as its own node), ref.
- **`LiteralMarker`** — the discriminator mechanism. Both dialects parse into the same node:
  single-value `enum` (513 today, 0 `const` **[verified]**) and `const` (138 in the observed
  newer dialect — doc 09), so the known drift costs a parser branch, not a redesign. Marker
  detection is mechanical — a required property with a literal value — not a hardcoded
  property-name list (`type`, `_tag`, `name`, and `status` are all found by the same rule).
- **Mechanical normalizations at parse time** (structural facts, no naming):
  - *Duplicate-`anyOf`-ref dedup.* The public API spec's `session.get` 404 example
    understates the phenomenon: 26 response-schema locations carry a duplicated ref in
    `anyOf` — `SessionNotFoundError` twice in 23 404s, `InvalidRequestError` twice in 3 400s
    **[verified]**. Dedup is one general rule; a post-dedup single-ref `anyOf` is a plain
    ref, not a union.
  - *Envelope shape classification* — `{data}` (12), `{data, location}` (20), `{cursor,
    data}` (behind named refs such as `SessionsResponse`), bare, none **[verified]** — the
    shape is structural; naming the payload is the Binder's job.
  - *Error-schema style detection* — of the 44 error-named schemas: 20 Effect-style `_tag`,
    17 `{name, data}`, 7 union/event wrappers **[verified]**, matching the public API spec
    §4.1 split. The two conventions are a structural fact recorded per schema; consumers
    never see the difference (ADR-0007).
- **Dialect wall.** The parser accepts only constructs it knows. Today the spec contains 0
  `allOf`, 0 `discriminator`, 0 type-arrays **[verified]**; if upstream starts emitting any
  of these — or an unrecognized content type, or a construct outside the known dialect — the
  parser refuses rather than mis-generates.

### 4.2 Binder and EmitPlan

The Binder is where every decision lands. Inputs: SpecIR + `curation.json`. Steps, in order:

1. **Load curation** with `JsonUnmappedMemberHandling.Disallow` — an unrecognized field in
   the config is itself an error (typos cannot be silently ignored), with
   `ReadCommentHandling.Skip` for comments.
2. **Bidirectional coverage checks** (§5.3), batched.
3. **Name computation** — mechanical PascalCase with `[JsonPropertyName]` wire fidelity;
   FDG acronym casing with curated brand exceptions (ADR-0004); dotted-schema-name mangling
   (7 dotted names: `session.status`, `question.replied`, `question.rejected`,
   `Event.tui.*` ×4 **[verified]**; doc 09's requirement); trailing-digit names
   (`ProviderAuthError1`, `UnknownError1`, `OutputFormat1` **[verified]**) pass through
   mechanically unless a curated schema-name override renames them; the bound-handle rule
   (operations with a `{sessionID}` path parameter emit into `SessionClient` — ADR-0008)
   plus curated handle children.
4. **Derived emission decisions** — which operations get forward-only paginators (those with
   the `{cursor, data}` envelope), which unions get tolerant converters (all of them —
   ADR-0009 is mechanical), registry membership, stream-endpoint item schemas (the SSE
   operations themselves are hand-wired, ADR-0008; the generator emits their item unions —
   `V2Event`, `Event`, `SessionDurableEvent`).
5. **Fingerprint computation** for every excluded or hand-wired operation (§9).

Output: **EmitPlan** — the complete file list with final names, namespaces, members, plus
registry, routes, paginator, and manifest contents. Emitters receive no open questions.

## 5. Curation config (sealed: JSON)

### 5.1 Format decision

`tools/curation.json`. C#-declarative config was considered (type-safe keys, compile-time
shape checking) and rejected on a mechanism the discussion surfaced: the decisive fail-closed
checks are *semantic* — "does this operationId exist in the spec", "is this envelope really
`{data}`" — and no compiler checks those in any format; the spec-side validator must exist
either way, and it catches shape/typo errors with better messages than the compiler would.
What remains for JSON is structural: logic in config is physically impossible rather than
convention-banned, and the "curation change = API review" rule (ADR-0008) reads cleanest on
pure-data diffs. Comments are carried as data (`reason` fields — mandatory on exclusions) and
STJ comment-skip permits real comments. Ecosystem precedent: Kiota, NSwag, and OpenAPI
Generator all use data files for generator config.

Upstream's counterpart is a tiny code-side options object —
`compile(api, { groupNames?, endpointNames?, omitEndpoints? })` — which suffices because
upstream generates from its own Effect contract where names are already authoritative. We
generate from their OpenAPI projection into a foreign language's idioms; our curation surface
is a few hundred rows, which justifies the dedicated data file.

### 5.2 Field inventory (v0 — grows extend-only, no speculative fields)

| Field | Content | Example |
|---|---|---|
| `groups` | wire group → client name; optional handle `{name, children}` | `session` → `Sessions`, handle `SessionClient`, children `[Permissions, Questions, Revert, Events]` |
| `envelopePayloadNames` | opId → payload property name | `v2.session.list` → `Sessions` |
| `exclusions` | `[{op, reason}]`, reason mandatory | `pty.connect` (both surfaces): WebSocket upgrade masquerading as GET (doc 10; public API spec §14) |
| `contentTypePayloads` | content type → `stream` / `string` | `application/octet-stream` → `stream`; `text/x-diff` → `string` **[verified: the only two non-JSON, non-SSE response content types]** |
| `parameterTypeOverrides` | `[{op, param, type}]` | `v2.session.history` `after`/`limit` → numeric (OpenAPI says string; Effect source is `NumberFromString` — public API spec §11.1) |
| `propertyOverrides` | per-property: `uri` marking, `uri→string` fallback, explicit-null | the 8 `anyOf`-null fields where null carries meaning (ADR-0004) |
| `schemaNameOverrides` | wire schema name → C# name | optional renames for trailing-digit names |
| `brandSpellings` | curated casing exceptions | `OAuth` (ADR-0004) |

### 5.3 Fail-closed mechanics — the drift radar, all four layers

**Layer 1 — Binder coverage checks (bidirectional set comparison, batched):**

| Direction | Check | Failure message shape |
|---|---|---|
| spec → curation | every operationId ∈ naming-map coverage ∪ exclusions | `operation 'v2.widget.list': no curation entry` |
| spec → curation | every operationId-prefix group has a `groups` row | `group 'widget': unnamed` |
| spec → curation | every enveloped response has a payload name | `v2.widget.list: payload unnamed` |
| spec → curation | every response content type ∈ map (JSON and SSE built in) | `image/png: unmapped content type` |
| spec → curation | every `anyOf`-null field has a null-semantics decision | ADR-0004: an unmapped `anyOf`-null fails generation |
| curation → spec | every curation key references an existing spec construct | `curation row 'session.prompt': matches nothing` (renames orphan their rows — orphans are errors, or the config rots silently) |

Upstream comparison: `httpapi-codegen` throws `GenerationError` on every ambiguity it meets
(name collisions, multiple payload schemas, unsupported encodings, missing path parameters,
wildcard paths in the Promise emitter) — the same refuse-to-guess philosophy — but its
`omitEndpoints` set has no reverse check: an orphaned omit entry is silently ignored
(`index.ts`, `compile()`). Our reverse direction closes that hole.

**Layer 2 — the parser's dialect wall** (§4.1): unknown constructs are refused, and the
zero-counts stay zero by force.

**Layer 3 — the fingerprint manifest** (§9): drift radar for exactly the constructs that
bypass generation.

**Layer 4 — CI:** regen-verify (`generate --verify`), the on-merit analyzer wall over
generated output (ADR-0003 — a new rule firing on generated code breaks the build), and
stale-file cleanup via the output manifest (removed operations cannot leave zombie API
behind).

## 6. Emitters

All emitters are deliberately dumb — they consume EmitPlan only, with no access to the spec or
curation — and are Roslyn syntax-factory emitters (ADR-0003), one per artifact family:

| Emitter | Emits | Governing rules |
|---|---|---|
| `ModelEmitter` | records: `init`-only, `required`, read-only collections, `[JsonPropertyName]`, `WhenWritingNull` on nullables, curated `Uri` properties | ADR-0004; public API spec §12 |
| `UnionEmitter` | union base + variants + the `Unknown*` carrier + one custom converter per union: buffer the element, read the tag position-independently, dispatch through the source-generated context; unknown tag → carrier (tag string + raw `JsonElement`), re-serialized as the raw payload | ADR-0009 |
| `EnvelopeEmitter` | per-op envelopes: guarded payload getters, internal `[SetsRequiredMembers]` error constructor, guarded `PrintMembers` override, `IDisposable` envelope for `Stream` payloads, `SessionsCursor`, payload-less 204 envelopes (19 modern 204 ops **[verified]**) | public API spec §5.1 |
| `InputRecordEmitter` | request input records (the ≤2-scalar flat-parameter rule is applied in the Binder; the emitter writes what the plan says) | public API spec §8.3 |
| `OperationMethodEmitter` | sub-clients, `SessionClient`, the `Legacy` hub; one-line delegating `virtual` methods, BCL throw-helper guards, XML docs from spec `summary`/`description`, `<exception cref>` lists from declared error responses | ADR-0008; public API spec §7/§8/§12.5–6 |
| `RoutesEmitter` | `OpenCodeRoutes` constants + `Uri.EscapeDataString` template methods | public API spec §8.6 |
| `RegistryEmitter` | the single `[JsonSerializable]` `JsonSerializerContext` registry | ADR-0003 (AOT commitment) |
| `PaginatorEmitter` | forward-only `IAsyncEnumerable<T>` paginators for `{cursor, data}` operations | public API spec §5.1 |

Trivia strategy per ADR-0003: XML documentation and directive trivia are emitted as parsed
strings (standard Roslyn practice — structured doc-trivia factories are impractical, doc 08);
`NormalizeWhitespace` plus the Writer's `dotnet format` post-step own final formatting.

## 7. File mechanics — on-merit output (resolves ADR-0003's deferred consequence)

On-merit conformance means the analyzer wall must fully engage on generated files, so the
generator deliberately avoids **all three** of Roslyn's generated-code auto-detection
heuristics (doc 07 §6, extracted from `GeneratedCodeUtilities.cs`):

1. **File name is plain `{TypeName}.cs`** — never `.g.cs`/`.generated.cs`/`.designer.cs`
   (those trigger generated-code treatment and drop project-level `<Nullable>`, doc 08).
   MA0048 (file = type) is an emission rule regardless.
2. **The header comment avoids the magic substring.** Emitted header:
   `// Generated by OpenCode.Sdk.Tools from spec/openapi.json.` /
   `// Do not edit by hand — change tools/curation.json or the emitters, then regenerate.`
   No `<auto-generated` token, so the heuristic stays cold while humans and agents still get
   the do-not-edit signal. The header deliberately carries no spec version: provenance lives
   in `spec/SNAPSHOT.md`, and a version string here would churn every generated file's diff
   on every refresh.
3. **No `[GeneratedCode]` attributes** — the third, per-symbol trigger.

Consequences: per-file `#nullable` directives do not exist (project-level `<Nullable>`
applies normally — ADR-0003's "fate of per-file `#nullable`" resolves to "not needed"); the
`.editorconfig` `[*.{g.cs,generated.cs,designer.cs}] generated_code = true` section stays for
genuine third-party generated files and never matches ours; IDE0130 requires namespace-true
folders, so the Writer places files by namespace (e.g. `Legacy/` for `OpenCode.Sdk.Legacy.*`,
public API spec §7.3) — generated-vs-hand-written is tracked by the output manifest, never by
folder convention.

## 8. Writer

1. **Output manifest** — `src/OpenCode.Sdk/.generated-manifest.json`, the sorted list of
   generated file paths, committed. Files present in the previous manifest but absent from
   the current plan are deleted (no zombie API); files outside the manifest are never
   touched (hand-written code is structurally safe). Pattern precedent: upstream's
   `.httpapi-codegen.json` manifest with stale-file removal and unsafe-path refusal
   (`index.ts`, `write()`).
2. **Determinism** (§2 principle 4): stable orderings, culture-invariant formatting, LF endings —
   byte-identical output for identical inputs.
3. **`dotnet format` post-step** runs on the whole SDK project rather than a per-file
   include list: hundreds of `--include` paths would strain the Windows command-line length
   limit, whole-project formatting is idempotent, and it matches what the CI format gate
   checks anyway. Upstream's equivalent is prettier-per-file inside `write()`.

## 9. Fingerprint manifest (ADR-0008 mechanics)

- **Location:** `spec/fingerprints.json` — next to the spec pin, because what it pins are
  spec subtrees. Committed; written only by the tool.
- **Scope:** every operation that bypasses generation — the curated exclusions
  (`v2.pty.connect`, legacy `pty.connect` — public API spec §14) and the four hand-wired SSE
  stream endpoints (§4.1 list). Entries carry `{surface, hash, reason}`.
- **Hash:** SHA-256 over the **canonical JSON** of the operation's spec subtree — the
  operation object, its parameters, its request/response content types, and every
  transitively referenced schema — with sorted keys and no whitespace, so cosmetic
  reordering of the spec file cannot move a hash.
- **Behavior:** `generate` recomputes fingerprints on every run. A mismatch, a missing
  entry (newly excluded op without a fingerprint), or an orphan entry (fingerprinted op no
  longer in the spec) is an error. The only way to update the manifest is the explicit
  `generate --update-fingerprints` flag, run after a human has reviewed whether the
  exclusion or hand-wiring still holds — the flag is the review gate itself. Upstream has no
  counterpart radar over its omitted endpoints; this mechanism is our addition (ADR-0008).

## 10. Spec refresh

**Command:** `refresh-spec --ref <tag|commit>` — (1) submodule fetch + checkout via CliWrap;
(2) copy `external/opencode/packages/sdk/openapi.json` → `spec/openapi.json`; (3) rewrite the
`SNAPSHOT.md` provenance table (commit, tag, `Date:`); (4) print an old-vs-new SpecIR diff
summary (added/removed/changed operationIds) as the refresh PR's review aid; (5) run
`generate`, which surfaces coverage gaps and fingerprint drift loudly (§5.3). This lands the
"dedicated spec-refresh tool" that `spec/SNAPSHOT.md` records as planned.

**Documentation decision (sealed): the refresh procedure is a playbook, not an ADR.** Against
`docs/adr/README.md`'s three-leg test it scores 0/3 — freely reversible, unsurprising, no
alternatives worth remembering — and the README explicitly routes process rules away from
ADRs. The decisions *inside* the flow are already recorded where they belong: tool ownership
of refresh in ADR-0003, the fingerprint review gate in ADR-0008. The playbook's canonical
home already exists: `AGENTS.md`'s sources-of-truth table names `spec/SNAPSHOT.md` for
"refresh steps", so the same change that lands the tool rewrites SNAPSHOT.md's refresh
section to the tool-based procedure: run `refresh-spec`, resolve what `generate` reports
(curation rows; `--update-fingerprints` after review), commit spec + curation + both
manifests + regenerated output as **one PR**. The refresh *cadence* question (snapshot per
SDK release) stays open in ROADMAP.

## 11. Testing — initial sketch only (deferred)

**Caveat (sealed):** the testing strategy and architecture — spanning `OpenCode.Sdk`,
`OpenCode.Sdk.Extensions`, and the tools; the unit/integration/functional split; real-process
integration harness and containerization — is owned by the queued **testing architecture &
strategy session** (ROADMAP queue 1 sequencing; ROADMAP Open Questions assigns it
explicitly). What follows is this session's initial sketch, recorded as *input* to that
session, to be deep-dived and possibly revised there — not sealed design.

- **Parser:** small hand-written spec fixtures, one per quirk (nested union, `anyOf`-null,
  duplicate-ref dedup, single-enum *and* `const` markers, dotted names, wildcard path, SSE
  detection, envelope shapes, both error styles) → assert SpecIR shape. Plus a full-spec
  smoke test: the pinned spec parses without error and structural invariants hold. Exact
  counts (188/61/127) stay out of tests — they are research-doc facts, and count assertions
  would turn every legitimate refresh into test noise.
- **Binder:** one red test per coverage check (missing group, unnamed envelope, orphan row,
  unknown config field — each verified present in the batched, categorized report); name
  computation cases (acronyms, dotted mangling, brand overrides); handle routing;
  paginator/converter derivation.
- **Emitters:** Verify snapshot tests (the ROADMAP-named tool), per-emitter, over small
  per-construct EmitPlan fixtures → `.verified.cs`. Micro snapshots localize failures; the
  macro snapshot already exists — the committed output in `src/`, guarded by regen-verify.
- **Compile gate:** no separate harness — the spike's strict-analyzer compile harness
  dissolves into the product build itself: committed output compiles in normal CI
  (5 TFMs × 3 OSes × the on-merit analyzer wall).
- **Round-trip behavior** (product tests, over the committed generated code): known tag →
  correct variant; unknown tag → `Unknown*` carrier and raw-payload re-serialization;
  out-of-order discriminators; explicit-null vs missing; guarded getter and guarded
  `PrintMembers` behavior.
- **Writer/commands:** MockFileSystem for manifest write and stale deletion; git/format
  calls behind `Infrastructure` wrapper interfaces, faked in tests; command wiring via
  `CommandAppTester`.
- **Determinism:** emit twice, assert byte-identical.

## 12. Multi-TFM emission (sealed: uniform source, zero `#if`)

Generated code is a single source set compiled by all five TFMs
(`netstandard2.0;net472;net8.0;net9.0;net10.0`, ADR-0002). The enablers are already locked:
the downlevel System.Text.Json package (source-gen, modern polymorphism,
`AllowOutOfOrderMetadataProperties` down to ns2.0 — doc 06 §4) and Polyfill (`required`,
`init`, records — ADR-0002). If a construct cannot be expressed uniformly downlevel, that is
a **generation error with an explanation**, never silent `#if` sprawl; conditional-emission
support would be added later only as a deliberate, recorded decision (extend-only).

The ROADMAP net472 spike item stands and sharpens: the spike compiled net10.0 only, so
"full-spec generated output compiles on all five TFMs" becomes an early build-out milestone,
before emitter polish.

## 13. CI wiring

One added step in the existing workflow: run `generate --verify` through the file-based entry
(CI dogfoods the entry) on a single OS — Linux, the fastest leg; output is platform-independent
text with LF endings enforced repo-wide, so multi-OS verification adds nothing. The existing
`dotnet format --verify-no-changes` gate already covers generated files (they are plain
`.cs`) — a deliberate second radar. The build itself is the compile gate for generated output
(§11).

## 14. Decisions sealed in this session (summary)

1. **Tools hosting:** synthesis — PathSmith-style csproj library + 3-line file-based entry
   delegating to a testable `ToolApp` factory; ADR-0003 packaging unchanged (§3.1).
2. **`tools/` is the centralized repo-tooling home;** the generator lives in a `Generator/`
   subtree as its first area, not as the project's identity (§2 principle 3).
3. **Curation config is JSON** — `tools/curation.json`, reasons as data, comment-skip,
   unknown-field disallow (§5.1).
4. **Coverage checks are bidirectional** — missing entries and orphan entries both fail
   (§5.3; exceeds upstream's one-directional `omitEndpoints`).
5. **Pipeline is two-stage** — Parser → SpecIR → Binder → EmitPlan → Emitters → Writer
   (§4).
6. **File mechanics:** plain `.cs` names, non-magic do-not-edit header, no
   `[GeneratedCode]`, no per-file `#nullable`; manifest tracks generated-ness (§7).
7. **Writer:** output manifest + stale cleanup; whole-project `dotnet format`;
   `--verify` = in-place regen + git-porcelain check (in-memory compare rejected — §3.2/§8).
8. **Fingerprints:** `spec/fingerprints.json`, canonical-JSON SHA-256 over operation
   subtrees, `--update-fingerprints` as the human review gate (§9).
9. **Refresh procedure is a playbook, not an ADR** — canonical home `spec/SNAPSHOT.md`,
   rewritten to the tool-based flow when the tool lands (§10).
10. **Multi-TFM: uniform source, zero `#if`;** inexpressible constructs fail generation
    (§12).
11. **Testing content is an initial sketch only**, owned by the queued testing architecture
    & strategy session (§11).

## 15. UNVERIFIED / open items

- File-based entry vs strict `Directory.Build.props`/CPM interplay (§3.3) — first build-out
  step; fallback recorded.
- Full-spec generated output on downlevel TFMs (§12) — early build-out milestone (ROADMAP
  net472 spike item).
- Refresh cadence policy (snapshot per SDK release?) — stays in ROADMAP Open Questions.
- Public namespace layout beyond the locked `OpenCode.Sdk.Legacy.*` subtree (public API spec
  §7.3) — the Writer needs the full namespace map; it is public API surface, so it lands as
  curation/`EmitPlan` detail reviewed like any naming decision at build-out.
- `dotnet format` invocation details (solution-vs-project scope, exit-code handling on
  format-only changes) — build-out detail inside the Writer.
