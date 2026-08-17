# Generator Architecture — the model-layer generator and repo tooling

Date: 2026-08-13

> **Status: vision / reference — not sealed.** Binding decisions live in the ADRs and
> `AGENTS.md`; this document is direction and design rationale, not law. Contradicting it
> is a finding to note, not a deviation-protocol event.
>
> The v1.x dual-surface facts here (operation counts, the legacy hub, envelope inventories)
> predate the 2026-08-13 v2 retarget (ADR-0005) and are historical; the mechanisms remain
> reference material.

Design specification produced by the generator-architecture brainstorm session (2026-08-09,
ROADMAP queue item 1). Every decision below was discussed and sealed individually with the
maintainer. This spec feeds a fresh-context grill session next; claims are therefore tied to
their evidence inline. Numeric claims marked **[verified]** were counted by script against
`spec/openapi.json` (v1.18.15) during this session; upstream-code claims cite exact files in
the `external/opencode` submodule read this session.

## 1. Scope and inputs

**In scope:** the internal architecture of the model-layer generator and the repo tooling that
hosts it — tooling layout and command surface, pipeline stages (ingestion/SpecIR, binder, emitters,
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
`<exception cref>` lists. The testing architecture spec
(`2026-08-10-testing-architecture-design.md` §7.1, §3) adds two test-consumed outputs —
the committed operation inventory and the contract test fixtures — tracked as a second
output root of the Writer's manifest machinery (§8). Output passes the analyzer wall on
merit (ADR-0003); a `dotnet format` post-step and CI regen-verify guard it.

**Evidence base:** ADRs 0003/0004/0005/0008/0009; research docs 08 (codegen spike — IR
boundary, emission trade, packaging), 09/10 (dialect drift, genealogy); the public API spec
(§5.1, §8, §11.2, §12, §14, §15); upstream's own generator
`external/opencode/packages/httpapi-codegen/src/index.ts` (1185 lines, read in full this
session); scripted verification against the pinned spec (results inline below).

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
4. **Determinism is a hard requirement.** Same spec + same curation + **same SDK feature
   band** ⇒ byte-identical output: stable file and member ordering, culture-invariant string
   operations, LF line endings. The SDK-band condition is honest, not hedging: Roslyn
   emission is pinned via CPM (`Microsoft.CodeAnalysis.CSharp`), but the `dotnet format`
   post-step ships inside the SDK and `global.json` rolls forward on `latestFeature`, so
   format behavior can shift across feature bands. CI regen-verify is built on this; the
   CI-resolved SDK is canonical (§13).
5. **Batched failure reporting.** Coverage violations are collected and reported together with
   category and subject — one fix-up PR per drift event, not a break/fix/break loop.

## 3. Tooling architecture

### 3.1 Layout (sealed)

```
tools/
├── opencode-tool.cs                  ← file-based entry (shebang, committed executable bit)
│     #!/usr/bin/env -S dotnet --
│     #:project ./OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj
│     return await OpenCode.Sdk.Tools.ToolApp.RunAsync(args);
├── curation.json                     ← curation config (§5)
└── OpenCode.Sdk.Tools/               ← csproj library, bound to the repo build rules
    ├── ToolApp.cs                    ← CommandApp + DI composition (testable factory)
    ├── GlobalSettings.cs             ← global log-level/log-file CLI settings
    ├── Commands/                     ← CLI surface for all tooling areas
    │   ├── GenerateCommand.cs        ←   generate [--verify] [--update-fingerprints]
    │   └── RefreshSpecCommand.cs     ←   refresh-spec --ref <tag|commit>
    ├── Generator/
    │   ├── Ingestion/                ← reader + projection + SpecIR types (§4.1)
    │   ├── Binding/                  ← Binder + curation models + EmitPlan types (§4.2)
    │   ├── Emission/                 ← per-artifact-family emitters (§6)
    │   └── Output/                   ← Writer: output manifest, stale cleanup, format (§8)
    └── Infrastructure/
        ├── GlobalOptionsInterceptor.cs
        ├── Logging/                  ← Spectre + optional file MEL providers
        └── …                         ← I/O, git/process wrappers

tests/OpenCode.Sdk.Tools.Tests/       ← TUnit + Spectre.Console.Cli.Testing + Testably.Abstractions.Testing
```

This resolves ADR-0003's "emission library behind a thin file-based entry" by synthesis:
the library carries a full DI composition per the repo's coding style
(`docs/engineering/coding-style.md` §2 — one `ServiceCollection` composition root behind
Spectre's `DependencyInjectionRegistrar`; `IFileSystem` and `IAnsiConsole`; MEL structured
logging with Spectre and optional Testably-backed file providers; global log-level/file
settings applied by an `ICommandInterceptor`; collaborators behind seams in per-slice
`Abstractions/` folders), but the
`CommandApp` wiring lives in a `ToolApp` factory so `Spectre.Console.Cli.Testing`'s
`CommandAppTester` can exercise it; the file-based entry shrinks to three lines and merely
delegates. The shebang uses the documented `-S dotnet --` form (without `--`, `dotnet` can
consume arguments that collide with its own CLI — `generate --verify` is exactly that
class). The committed executable bit lives only in the git index
(`git update-index --chmod=+x`) — meaningless on a Windows filesystem, effective on the
Linux CI checkout that dogfoods the entry (§13). ADR-0003's packaging clause survives
unchanged.

Two deliberate composition choices: `ToolApp` factory instead of logic in
`Program.cs` (testability of the wiring); pipeline-stage folders under `Generator/` instead of
a flat `Services/` (MA0048/IDE0130 make file=type and folder=namespace mandatory anyway, and
the generator is one tooling area among future siblings — §2 principle 3).
`ToolApp.CreateServices` is the only registration path: production builds the registrar from
it, while tests apply seam replacements after the same registrations rather than maintaining
a second composition root.

**Stack** (ROADMAP queue 2, confirmed): Spectre.Console.Cli (+ the DI registrar + Testing
packages), Microsoft.Extensions.Logging, CliWrap for git and `dotnet format` invocations
(the no-CliWrap rule is SDK-product-scoped, ADR-0001), Testably.Abstractions in production,
Testably.Abstractions.Testing in tests, and the independent TestableIO analyzer for all
filesystem I/O (public API spec §3), the pinned Microsoft.OpenApi reader for spec ingestion,
System.Text.Json for curation parsing, Microsoft.CodeAnalysis.CSharp for emission
(ADR-0003). All versions enter `Directory.Packages.props`.

### 3.2 Command surface

- **`generate`** — parse → bind → emit → write. Fingerprint verification (§9) runs inside
  every invocation, not as a separate command.
  - `--verify` — CI/regen-verify mode: snapshot old/new manifest-owned paths, regenerate and
    format in place, byte-compare the final owned artifacts, and return nonzero with the
    created/changed/deleted path list when drift existed. It repairs drift as part of the run
    and has no Git or clean-worktree precondition.
  - `--update-fingerprints` — the explicit human review gate for fingerprint drift (§9).
- **`refresh-spec --ref <tag|commit>`** — the spec-refresh workflow (§10).

### 3.3 Verification list — carried to build-out

File-based apps are documented to inherit `Directory.Build.props`, `Directory.Packages.props`,
and `global.json` from parent directories (Microsoft Learn, file-based apps, 2026-04), so the
entry *will* face the analyzer wall, TWAE, and the LangVersion pin; binding it to the repo
rules is deliberate — the same page recommends isolating file-based apps from implicit build
files, and we knowingly do the opposite. The entry uses only `#:project`, no `#:package`, so
CPM version friction does not arise. Three items must be proven as the first build-out step:

1. The entry builds clean under the strict props (the original item).
2. **Cache staleness.** The SDK's file-based build cache keys on source content, directives,
   SDK version, and implicit build files — a change in the `#:project`-referenced library is
   not documented to trigger a rebuild, and a stale tool silently running old emitter code is
   unacceptable for a generator. If rebuilds are not triggered, entry invocations are routed
   through an explicit `dotnet build` first, or the fallback fires.
3. Invocation form: with a csproj in the working directory, `dotnet run file.cs` runs the
   *project* and passes the file as an argument (documented backwards compatibility) — CI and
   docs pin the `dotnet run --file` / direct `./tools/opencode-tool.cs` forms.

The recorded fallback is unchanged — drop the entry, promote the library to a console app (a
one-line ADR-0003 correction, maintainer-approved), `ToolApp` untouched. Its trigger is now
two-condition: the strict-props build cannot be made clean, **or** cache staleness has no
reliable workaround.

## 4. Pipeline architecture (sealed: two-stage)

```
spec/openapi.json ─▶ Microsoft.OpenApi ─▶ typed DOM ─▶ Projection ─▶ SpecIR ─▶ Binder ─▶ EmitPlan ─▶ Emitters ─▶ Writer
                     reader (pinned:       (tool-       (dialect wall,   (minimal,   │ + curation.json     (Roslyn)    │ output manifest
                     parsing, $refs,        internal)    normalizations,  immutable)  │ all fail-closed                 │ stale cleanup
                     lossless retention)                 batched errors)              ▼ checks, batched                 ▼ dotnet format
                                                                                   batch error report            src/OpenCode.Sdk/
```

The alternative — a single stage applying curation during projection — was considered and rejected:
curation errors would surface one at a time mid-walk, wire analysis and C# naming would
interleave, and the ingestion/emitter reversal boundary that ADR-0003 records would blur.
Feeding the Microsoft.OpenApi DOM directly into the Binder (no SpecIR) was considered and
rejected on run evidence: the reference/concrete dichotomy and the library's
flags-and-defaults idioms would leak into every Binder rule, the same analyses would
re-traverse the DOM repeatedly, every Binder test would need reader-built fixtures, and the
refresh diff would have no stable model to serialize (research log session 12). The two-stage shape is also upstream's
own: `httpapi-codegen` separates `compile()` (producing a `Contract` IR of groups, endpoints,
and operations) from `emitPromise`/`emitEffect`/`write` over that IR
(`packages/httpapi-codegen/src/index.ts`).

### 4.1 Ingestion and SpecIR

**Reader.** The pinned `Microsoft.OpenApi` package (CPM-pinned; tooling-only — it never
enters the shipped packages) parses `spec/openapi.json` into its typed DOM via
`OpenApiDocument.LoadAsync` over a stream opened through the Testably filesystem seam
(`LeaveStreamOpen` honored **[verified]**). The library owns lexical JSON parsing,
OpenAPI container parsing, `$ref` construction and resolution, and lossless retention
of unknown schema keywords (`UnrecognizedKeywords`) and vendor extensions. Three
reader-level rules are wall layers of their own:

1. **Version gate:** a document whose `SpecificationVersion` is not `OpenApi3_1`
   refuses — the DOM does not retain the raw `openapi` string, and a `3.2.0` document
   otherwise parses clean with typed 3.2 members populated
   (`OpenApiMediaType.ItemSchema`) and zero diagnostics **[verified]**.
2. **Reader diagnostics are errors:** any reader diagnostic fails generation. Unknown
   non-`x-` keys at non-schema hosts surface *only* there — the DOM silently drops the
   key, and the diagnostic's location rides in its message text, not its pointer
   **[verified]**.
3. **Reader exceptions translate:** `LoadAsync` failures become located ingestion
   errors, never raw exceptions — a boolean property schema (legal JSON Schema
   2020-12) crashes the pinned reader with a `NullReferenceException`
   **[verified]**; red test in §11, candidate upstream bug report.

Diagnostics are still not sufficient: the reader accepts constructs our dialect
refuses with zero diagnostics (run-proven: raw `prefixItems`, injected
`allOf`/`if`/type-arrays/`discriminator`, typed `headers`/`callbacks`/`webhooks`/
`$defs` payloads all load clean) — hence the projection wall below.

**Projection.** Explicit projectors produce minimal SpecIR plus batched, located errors.
Validation is semantic-risk fail-closed: a selected construct refuses when dropping it could
change emitted wire or public behavior, while descriptive or unconsumed library metadata does
not create a maintenance wall. Standing guards cover reader version, diagnostics, exception
translation, unsupported selected operation/media shapes, unresolved references, `$ref`
siblings, the pinned raw `prefixItems` adapter, and no Microsoft.OpenApi leakage beyond
ingestion. Generated-source diffs plus focused semantic/runtime tests are the primary drift
radar; there is no reflection member/default inventory or full SpecIR snapshot.

Extension handling is explicit: `x-codeSamples` is known-ignored, `x-websocket` projects
operation behavior, and `x-effect-stream` is carried on SSE media. Other extensions are located
informational drift unless a selected projection would otherwise lose behavior it claims to
emit.
- **Unrestricted schemas.** A schema with **no admitted constraint member**
  populated (annotations such as `description` permitted) accepts any JSON value
  and projects to an explicit any-value node — 19 sites in the pin, including
  union-branch positions (`Workspace.extra/anyOf/0`) **[verified — exhaustive
  DOM+raw correlation]**. Mapping `{}` to a free-form *object* node would silently
  narrow the wire.
- **`prefixItems` adapter.** The one pinned construct the library leaves untyped:
  the raw items parse through the supported fragment API
  (`OpenApiModelFactory.Parse<OpenApiSchema>` with host-document context
  **[verified]**) into the tuple node.
- **Mechanical normalizations** — projection rules, run-verified against the pin by
  the landmark prototype: duplicate-`anyOf`-ref dedup (26 sites; a post-dedup
  single-ref `anyOf` is a plain ref), envelope-shape classification (`{data}`,
  `{data, location}`, `{cursor, data}`, `{data, hasMore}`, bare, none — envelope
  shapes occur only at modern response roots **[verified]**), error-style detection
  (20 Effect-`_tag` + 17 `{name, data}`), literal markers in both dialects
  (single-value `enum` today; `const` accepted for the observed newer dialect —
  admitted only on string-typed schemas, because the typed `Const` member is a
  string and cannot preserve other literal kinds **[verified]**), special-value
  numbers, parameter-stripped media-type matching, SSE detection by
  `text/event-stream` (4 ops; `x-effect-stream` only on `v2.session.events`), the
  wildcard path flag (`/api/fs/read/*` is the only wildcard path), dotted schema
  names kept verbatim. **Union classification** separates *marked* unions (every
  object branch carries a literal marker — these take ADR-0009's tag-dispatch
  converters) from *structural* unions (heterogeneous branch kinds without markers
  — 5 pin sites, e.g. `Config.formatter`'s `bool | dict`); the distinction is a
  recorded SpecIR fact the Binder consumes.

**SpecIR.** SpecIR is the Binder's sole spec-side input — a *minimal semantic
projection*, not a wire-faithful parse tree. It stays immutable and free of C#
concepts, and the mutable Microsoft.OpenApi DOM never crosses it: the projection is
the only code that touches library types, guarded by two structural tests (§11) —
no library type reachable from SpecIR's public surface, no library `using` outside
`Generator/Ingestion/`. It carries the operation surface (operationId;
`Modern`/`Legacy` keyed on the `v2.` operationId prefix, never the path; HTTP
method; path template with the wildcard flag; parameters incl. deepObject; request
body; responses with status, stripped media type, schema and envelope shape;
SSE/WebSocket flags; the opaque `x-effect-stream` value; deprecation and doc text)
and the schema graph — named schemas under verbatim wire names plus promoted inline
types under deterministic `{root}#{pointer}` keys (marker-keyed union branches with
an ordinal-index fallback for unmarked branches; JSON-pointer `~0`/`~1` escaping;
never a document-global counter) — classified into the semantic node kinds the
Binder consumes: object (including the six hybrid objects carrying both properties
and an `additionalProperties` schema), dictionary, free-form, **unrestricted**,
union (marked/structural), enum, literal, special-number, tuple,
   content-encoded-string, nullable, primitive, ref. Nodes carry `description` and
   `format` (Binder doc-text and payload-rule inputs). Hashes are not carried before the
   excluded/hand-wired fingerprint feature has a consumer. Ordering:
schema-graph keys sort ordinal; operation lists and object member order are
   document order — generated-source determinism tests guard the ordering that reaches output.
The exact record inventory is derived backward from Binder, emitter and
refresh-diff consumption at slice planning — a field nothing consumes is not
carried.

**No longer ours** (and never re-tested here — testing spec §10): lexical JSON
parsing and syntax diagnostics, generic OpenAPI document/operation/parameter/
response parsing, `$ref` mechanics, generic OpenAPI conformance validation,
lossless retention of unknown keys.

### 4.2 Binder and EmitPlan

The Binder is where every decision lands. Inputs: SpecIR + `curation.json`. Steps, in order:

1. **Load curation** with `JsonUnmappedMemberHandling.Disallow` — an unrecognized field in
   the config is itself an error (typos cannot be silently ignored) — plus
   `ReadCommentHandling.Skip` and `AllowTrailingCommas` for hand-edit ergonomics
   (run-verified 2026-08-09: the three options compose). The curation models pin their
   wire names with `[JsonPropertyName]` — under `Disallow` even a naming-policy mismatch
   is a loud error; same wire-fidelity discipline as the generated models.
2. **Bidirectional coverage checks** (§5.3), batched.
3. **Emission scope — reachable closure, computed on every run.** The emitted schema set
   is the transitive `$ref` closure of every included operation (parameters, request
   bodies, all response schemas, SSE item schemas); excluded operations contribute
   nothing (their subtrees are fingerprint-pinned instead, §9). **Envelope-classified
   response-root named schemas are not emitted as models** (`SessionsResponse`,
   `SessionHistory`, `SessionMessagesResponse` **[verified: exactly one inbound ref
   each, from their response roots]**): the `EnvelopeEmitter` owns their shape — only
   their payload/cursor/location subtrees join the closure, and a non-envelope inbound
   reference to such a schema is a batched error. Named schemas outside the
   closure (13 today, incl. `OutputFormat1` **[verified]**) are not emitted and are
   reported as an info-level list in the generate output — never maintained as a list
   anywhere; a refresh that wires an orphan schema into an operation pulls it into the
   closure automatically. Curation reverse checks (§5.3) validate against the closure, so
   a row referencing an unreachable schema is an error. Upstream corroborates the
   dead-schema stance: its own SDK build deletes unreachable `SessionNext*1` schemas from
   the document before generation (`packages/sdk/js/script/build.ts`).
4. **Name computation** — mechanical ordinary PascalCase, including acronym tokens, with
   `[JsonPropertyName]` wire fidelity and curated brand exceptions (ADR-0004); dotted-schema-name mangling
   (7 dotted names: `session.status`, `question.replied`, `question.rejected`,
   `Event.tui.*` ×4 **[verified]**; doc 09's requirement); trailing-digit names
   (`ProviderAuthError1`, `UnknownError1` **[verified]**; the third such name,
   `OutputFormat1`, sits outside the reachable closure and is never emitted) pass through
   mechanically unless a curated schema-name override renames them; curation declares each
   bound handle's collection name, handle name, and required path parameter, and the Binder
   partially applies that parameter without operation-specific branches (ADR-0008).
5. **Derived emission decisions** — which operations get forward-only paginators (those with
   the `{cursor, data}` envelope), which unions get tolerant converters (all *marked*
   unions — ADR-0009's rule is mechanical over them; the structural-union emission
   shape is an open item, §15), registry membership, stream-endpoint item schemas (the SSE
   operations themselves are hand-wired, ADR-0008; the generator emits their item unions —
   `V2Event`, `Event`, `SessionDurableEvent`). **XML doc computation** happens here too:
   every public type and member gets its doc text in the EmitPlan — spec
   `summary`/`description` where present (XML-escaped), a deterministic synthesized
   fallback from names and structure otherwise. The fallback is the majority path for
   models: the pinned spec documents 185/188 operations but only 3/472 schemas and 27/1836
   properties **[verified]** — CS1591 stays `error` with no exemption, so emission must
   cover every public member.
6. **Fingerprint computation** is deferred until the first excluded or hand-wired operation
   enters the intended product surface (§9); no unused hash facts live in SpecIR beforehand.

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
pure-data diffs. Comments are carried as data (`reason` is mandatory on exact schema aliases),
and STJ comment-skip permits real comments. Ecosystem precedent: Kiota, NSwag, and OpenAPI
Generator all use data files for generator config.

Upstream's counterpart is a tiny code-side options object —
`compile(api, { groupNames?, endpointNames?, omitEndpoints? })` — which suffices because
upstream generates from its own Effect contract where names are already authoritative. We
generate from their OpenAPI projection into a foreign language's idioms; our curation surface
is a few hundred rows, which justifies the dedicated data file.

### 5.2 Field inventory (v0 — grows extend-only, no speculative fields)

| Field | Content | Example |
|---|---|---|
| `groups` | modern wire group → placement and client name; paired optional `handleName` + required path `handleParameter` | `session` → client `Sessions`, handle `SessionClient`, parameter `sessionID` |
| `envelopePayloadNames` | opId → payload property name | `v2.session.list` → `Sessions` |
| `schemaAliases` | source schema → structurally identical target, with mandatory reason | `Tool.FileContent1` → `Tool.FileContent`; the binder proves deep equality before collapse |

Operation exclusions remain full-subtree fingerprint entries in the generated manifest rather
than semantic curation. Wire types, formats, constraints, and validation come only from the pinned
OpenAPI document; a missing projection stays faithful or fails closed (ADR-0013).

### 5.3 Fail-closed mechanics — the drift radar, all four layers

**Layer 1 — Binder coverage checks (bidirectional set comparison, batched):** during staged
generation, spec-to-curation checks cover only selected operations and their reachable closure;
curation-to-spec checks remain global so orphan rows always fail. The release profile expands
the forward checks to both complete surfaces. Legacy placement remains mechanically flat and
does not require modern group-handle rows.

| Direction | Check | Failure message shape |
|---|---|---|
| selected spec → curation | every selected modern group has a `groups` row | `group 'widget': unnamed` |
| selected spec → curation | every selected enveloped response has a payload name | `v2.widget.list: payload unnamed` |
| selected spec → curation | every selected response content type is supported (JSON and SSE built in) | `image/png: unmapped content type` |
| curation → spec | every curation key references an existing spec construct | `curation row 'session.prompt': matches nothing` (renames orphan their rows — orphans are errors, or the config rots silently) |

Upstream comparison: `httpapi-codegen` throws `GenerationError` on every ambiguity it meets
(name collisions, multiple payload schemas, unsupported encodings, missing path parameters,
wildcard paths in the Promise emitter) — the same refuse-to-guess philosophy — but its
`omitEndpoints` set has no reverse check: an orphaned omit entry is silently ignored
(`index.ts`, `compile()`). Our reverse direction closes that hole.

**No behavior-premised overrides.** Curation cannot repair projection loss from implementation
source, prose, or names. Alias reasons explain why a collapse exists, while deep structural
comparison is the executable wall that proves the source and target remain equivalent.

**Layer 2 — the projection's dialect wall** (§4.1): unknown constructs are refused, and the
zero-counts stay zero by force.

**Layer 3 — the fingerprint manifest** (§9): drift radar for exactly the constructs that
bypass generation.

**Layer 4 — CI:** regen-verify (`generate --verify`), the on-merit analyzer wall over
generated output (ADR-0003 — a new rule firing on generated code breaks the build), and
stale-file cleanup via the output manifest (removed operations cannot leave zombie API
behind).

In-schema drift of **generated** operations is deliberately assigned to no bespoke
detector: it surfaces as the regenerated-output diff in the refresh PR (the own-generator
decision's core payoff — ADR-0008's loud diff), backed by the analyzer wall and the
round-trip behavior tests; the refresh-spec diff summary (§10) flags the affected
operationIds for the reviewer.

## 6. Emitters

All emitters are deliberately dumb — they consume EmitPlan only, with no access to the spec or
curation — and are Roslyn syntax-factory emitters (ADR-0003), one per artifact family:

| Emitter | Emits | Governing rules |
|---|---|---|
| `ModelEmitter` | records: `init`-only, `required`, read-only collections, `[JsonPropertyName]`, `WhenWritingNull` on nullables, `Uri` only for OpenAPI `format: uri` | ADR-0004; public API spec §12 |
| `UnionEmitter` | union base + variants + the `Unknown*` carrier + one custom converter per union: buffer the element, read the tag position-independently, dispatch via a per-union static tag→type map through the source-generated context; unknown tag → carrier (tag string + raw `JsonElement`), re-serialized as the raw payload. No `[JsonPolymorphic]`/`[JsonDerivedType]` attributes — the converter is the single dispatch owner; the discriminator is emitted as a get-only computed property (serialized on write, ignored on read). Spike-proven (2026-08-09, 88-variant harness under the full wall, reflection fallback disabled): map shape 0/0 with constant-size `Read`/`Write`; type-based context dispatch is AOT-safe; the 88-arm switch shape measured failing MA0051 (96 lines) and was rejected — dispatch as data, not control flow | ADR-0009 (marked unions; structural-union shape: §15) |
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
   the current plan are deleted (no zombie API); the Writer never **writes or deletes**
   outside the manifest. Mixed generated/hand-written roots additionally refuse unmanifested
   overwrites and require the exact provenance header before overwrite or deletion. Pattern precedent:
   upstream's `.httpapi-codegen.json` manifest with stale-file removal and unsafe-path
   refusal (`index.ts`, `write()`). The test-consumed artifacts (operation inventory and
   contract fixtures — testing spec §7.1/§3) form a second manifest root under the same
   write/delete discipline.
2. **Determinism** (§2 principle 4): stable orderings, culture-invariant formatting, LF endings —
   byte-identical output for identical inputs.
3. **`dotnet format` post-step** receives only the current manifest-owned source paths through
   `--include`, so generation cannot rewrite hand-written WIP. `--verify` snapshots old/new
   owned paths, regenerates and formats them, then byte-compares without depending on Git.
   Upstream's equivalent is prettier-per-file inside `write()`.

## 9. Fingerprint manifest (ADR-0008 mechanics)

This section is a deferred design. It has no Arc B consumer and does not add fields to current
SpecIR. Before the first excluded or hand-wired operation enters the intended full surface, the
implementation plan must choose and test a canonical subtree acquisition mechanism without
making Binder or emitters parse OpenAPI.

- **Location:** `spec/fingerprints.json` — next to the spec pin, because what it pins are
  spec subtrees. Committed; written only by the tool.
- **Scope:** every operation that bypasses generation — the curated exclusions
  (`v2.pty.connect`, legacy `pty.connect` — public API spec §14) and the four hand-wired SSE
  stream endpoints (§4.1 list). Entries carry `{surface, kind, hash, reason}`; `kind`
  (`excluded` | `handwired`) selects the hash coverage.
- **Hash:** SHA-256 over canonical raw subtrees acquired by the fingerprint feature:
  canonical JSON per raw subtree — sorted keys, no whitespace, so cosmetic reordering
  of the spec file cannot move a hash); the composition depends on `kind`:
  - `excluded` — the full subtree: the **HTTP method and path** (included explicitly — the
    OpenAPI operation object carries neither, so a same-shape move or method change would
    otherwise slip the radar), the operation object, its parameters, its request/response
    content types, and every transitively referenced schema. Nothing here is generated;
    everything stays on the radar.
  - `handwired` — the **transport shape** only: method, path, parameters, content types,
    the `x-effect-stream` value, and the *names* (not contents) of the item-union schemas.
    The item schemas are generated and already guarded by regen-verify; hashing their
    transitive closure would break the fingerprint on nearly every refresh (the event
    unions reach half the spec) and erode the `--update-fingerprints` review gate into
    reflex approval. The fingerprint pins exactly what generation does not cover — the
    contract the hand-written wiring assumes.
- **Behavior:** `generate` recomputes fingerprints on every run. A mismatch, a missing
  entry (newly excluded op without a fingerprint), or an orphan entry (fingerprinted op no
  longer in the spec) is an error. The only way to update the manifest is the explicit
  `generate --update-fingerprints` flag, run after a human has reviewed whether the
  exclusion or hand-wiring still holds — the flag is the review gate itself. Upstream has no
  counterpart radar over its omitted endpoints; this mechanism is our addition (ADR-0008).

## 10. Spec refresh

**Command:** `refresh-spec --ref <tag|commit>` — (1) submodule fetch + checkout via CliWrap;
(2) copy `external/opencode/packages/sdk/openapi.json` → `spec/openapi.json`; (3) rewrite the
`SNAPSHOT.md` provenance table (commit, tag, `Date:`) and stamp the opencode test-server
version pin into the machine-readable `spec/opencode-version` file, relayed by `SNAPSHOT.md`
(single-sourced with the spec pin — testing spec §11); (4) print an old-vs-new SpecIR diff
summary (added/removed/changed operationIds) plus the ingestion inventory delta (new
keywords, extensions, unrestricted sites) as the refresh PR's review aid; (5) run
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

## 11. Testing — tooling test design (sealed)

The testing strategy and architecture across all packages is owned by the testing
architecture spec (`2026-08-10-testing-architecture-design.md`); its §10 sealed this
section's content and added three revisions: tests for the tool's two new outputs (the
operation inventory and the contract fixtures — §1/§8), `refresh-spec` command tests, and
the round-trip behavior tests reassigned to `OpenCode.Sdk.Tests` (level 1 there). What
follows is the sealed tooling-test design.

- **Projection:** small hand-written spec fixtures, one per quirk (unrestricted `{}`
  incl. a union-branch position, nested union, `anyOf`-null, duplicate-ref dedup,
  single-enum *and* `const` markers, dotted names, wildcard path, SSE detection,
  envelope shapes, both error styles, the `prefixItems` tuple), loaded **through the
  pinned reader** (published contract — DOM types are never faked) → assert SpecIR
  shape. Wall red tests: every refused construct class (`allOf`, type arrays,
  `discriminator`, the 2020-12 applicators, unknown `x-*` per host, unresolved refs)
  verified batched and located. The §4.1 library-upgrade tripwires live here, plus one
  malformed-JSON diagnostic-translation test and the boolean-schema reader-crash red
  test (a located ingestion error, never a raw exception). Two structural guard tests
  enforce the DOM boundary: a reflection test over SpecIR's public surface (no
  Microsoft.OpenApi type reachable) and a source scan (no Microsoft.OpenApi `using`
  outside `Generator/Ingestion/`). Plus a full-spec smoke test: the pinned
  spec projects without error and structural landmarks hold. Exact counts (188/61/127)
  stay out of tests — they are research-doc facts, and count assertions would turn
  every legitimate refresh into test noise. Microsoft.OpenApi internals are never
  re-tested. Small one-off variations compose the shared domain builder inline; a named
  scenario class appears only when the arrangement is reused across test classes,
  non-trivial, or a durable cross-slice landmark (`testing-style.md` §1).
- **Binder:** one red test per coverage check (missing group, unnamed envelope, orphan row,
  unknown config field — each verified present in the batched, categorized report); name
  computation cases (acronyms, dotted mangling, brand overrides); handle routing;
  paginator/converter derivation.
- **Emitters:** Verify snapshot tests (the ROADMAP-named tool), per-emitter, over small
  per-construct EmitPlan fixtures → `.verified.cs`. Micro snapshots localize failures; the
  macro snapshot already exists — the committed output in `src/`, guarded by regen-verify.
- **Compile gate** (architecture, not testing strategy — it follows from §13 and is not
  the queued session's to revisit): no separate harness — the spike's strict-analyzer
  compile harness dissolves into the product build itself: committed output compiles in
  normal CI (5 TFMs × 3 OSes × the on-merit analyzer wall).
- **Inventory & fixtures:** inventory fidelity to curation (excluded ops absent, SSE ops
  flagged); fixture-synthesis determinism by direct byte comparison.
- **Writer/commands:** MockFileSystem for manifest write and stale deletion; git/format
  calls behind `Infrastructure` wrapper interfaces, faked in tests; command wiring via
  `CommandAppTester`; `refresh-spec` covered the same way (faked git/copy wrappers,
  `SNAPSHOT.md` rewrite, diff-summary output).
- **Determinism:** emit twice, assert byte-identical.

## 12. Multi-TFM emission (sealed: uniform source, zero `#if`)

Generated code is a single source set compiled by all five TFMs
(`netstandard2.0;net472;net8.0;net9.0;net10.0`, ADR-0002). The enablers are already locked:
the downlevel System.Text.Json package (source-gen down to ns2.0 — doc 06 §4) and Polyfill
(`required`, `init`, records — ADR-0002). If a construct cannot be expressed uniformly
downlevel, that is a **generation error with an explanation**, never silent `#if` sprawl;
conditional-emission support would be added later only as a deliberate, recorded decision
(extend-only).

The converter design (ADR-0009; dispatch shape sealed in §6) retires two of the ROADMAP
net472 spike unknowns: `[JsonPolymorphic]` is never emitted, and
`AllowOutOfOrderMetadataProperties` is not needed — the converter buffers the element and
reads the tag itself (spike-proven, tag-last case). The surviving downlevel checklist:

1. `required`/`init`/records via Polyfill (unchanged).
2. The STJ surface the converters actually use, on the downlevel package:
   `Utf8JsonReader`/`JsonDocument` buffering,
   `JsonNumberHandling.AllowNamedFloatingPointLiterals` (§4.1's special-value numbers),
   `[JsonStringEnumMemberName]` for the 21 non-identifier enum values **[verified]**.
3. The dispatch map's collection type on ns2.0: the emitter writes a plain read-only
   `Dictionary<string, Type>` — `FrozenDictionary` would drag a
   `System.Collections.Immutable` dependency into downlevel TFMs, and the difference is
   unmeasurable at our sizes.

The ROADMAP net472 spike item stands and sharpens: the spike compiled net10.0 only, so
"full-spec generated output compiles on all five TFMs" remains an early build-out milestone,
before emitter polish.

## 13. CI wiring

One added step in the existing workflow: run `generate --verify` through the file-based entry
(CI dogfoods the entry) on a single OS — Linux, the fastest leg; output is platform-independent
text with LF endings enforced repo-wide (`.gitattributes` + `.editorconfig` `end_of_line`), so
multi-OS verification adds nothing. **The SDK that CI resolves is canonical:** when a local
`generate` and CI `--verify` disagree, the first suspect is SDK feature-band skew
(`global.json` rolls forward on `latestFeature` — §2 principle 4), and the fix is aligning
the local SDK, never hand-editing output. The existing `dotnet format --verify-no-changes`
gate already covers generated files (they are plain `.cs`) — a deliberate second radar. The
build itself is the compile gate for generated output (§11).

## 14. Sealed decisions (summary)

1. **Tools hosting:** synthesis — a full DI-composed csproj library + 3-line file-based
   entry delegating to a testable `ToolApp` factory; one production/test registration path,
   filesystem/console seams, MEL Spectre/optional-file providers, global settings, and the
   interceptor are part of that host; ADR-0003 packaging unchanged (§3.1).
2. **`tools/` is the centralized repo-tooling home;** the generator lives in a `Generator/`
   subtree as its first area, not as the project's identity (§2 principle 3).
3. **Curation config is JSON** — `tools/curation.json`, reasons as data (mandatory on
   structurally verified schema aliases), comment-skip + trailing commas,
   unknown-field disallow, wire names pinned with `[JsonPropertyName]` (§5.1, §5.3;
   options run-verified).
4. **Coverage checks are bidirectional** — missing entries and orphan entries both fail
   (§5.3; exceeds upstream's one-directional `omitEndpoints`).
5. **Pipeline is two-stage** — Microsoft.OpenApi reader → projection → SpecIR →
   Binder → EmitPlan → Emitters → Writer (§4; ingestion: ADR-0003). Ingestion
   mechanics retain the `OpenApi3_1` version gate, reader diagnostics as errors, targeted
   semantic guards, DOM-boundary tests, and envelope-root subtraction (§4.1, §4.2, §11).
6. **File mechanics:** plain `.cs` names, non-magic do-not-edit header, no
   `[GeneratedCode]`, no per-file `#nullable`; manifest tracks generated-ness (§7).
7. **Writer:** output manifest + stale cleanup; owned-path `dotnet format`; `--verify` =
   in-place regen plus before/after byte comparison without Git (§3.2/§8).
8. **Fingerprints are deferred until consumed:** the first excluded or hand-wired operation
   lands `spec/fingerprints.json`, canonical subtree hashing, two-kind coverage, and the human
   update gate together (§9).
9. **Refresh procedure is a playbook, not an ADR** — canonical home `spec/SNAPSHOT.md`,
   rewritten to the tool-based flow when the tool lands (§10).
10. **Multi-TFM: uniform source, zero `#if`;** inexpressible constructs fail generation
    (§12).
11. **Testing is owned by the testing architecture spec** (2026-08-10); §11 carries the
    sealed tooling-test design with its three revisions.

## 15. UNVERIFIED / open items

- Structural-union emission shape — the five pin sites whose branches carry no literal
  markers (`Config.formatter`, `Config.lsp` ×2, `Model.capabilities.interleaved`, the
  `ProviderConfig` model-variant `interleaved`): `JsonElement`-backed carrier vs a
  generated wrapper type with a bespoke converter — decided at slice 2/3 planning as an
  API review (curation changes are API reviews, ADR-0008).
- File-based entry verification list (§3.3: clean build under the strict props, `#:project`
  cache-staleness proof, invocation-form pinning) — first build-out step; two-condition
  fallback recorded.
- Full-spec generated output on downlevel TFMs (§12) — early build-out milestone (ROADMAP
  net472 spike item).
- Refresh cadence policy (snapshot per SDK release?) — stays in ROADMAP Open Questions.
- Public namespace layout beyond the locked `OpenCode.Sdk.Legacy.*` subtree (public API spec
  §7.3) — the Writer needs the full namespace map; it is public API surface, so it lands as
  curation/`EmitPlan` detail reviewed like any naming decision at build-out.
- `dotnet format` invocation details (solution-vs-project scope, exit-code handling on
  format-only changes) — build-out detail inside the Writer.
