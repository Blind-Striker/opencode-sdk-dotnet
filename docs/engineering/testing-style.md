# Testing Style — authoring tests

Date: 2026-08-17

Binding authorship style for every test in this repository. The testing *architecture* —
levels, projects, the dual-mode harness, coverage gates, CI — is owned by the sealed
testing design (`AGENTS.md` testing posture; fine-grained design currently in the
transient design specs, reachable via `docs/ROADMAP.md`, distilled at build-out); this
document governs how tests are *written* inside that architecture, and it applies
`coding-style.md` to test code — test infrastructure is code and follows every rule
there.

## 1. Test infrastructure is a first-class citizen

Test setup is designed, reviewed, and refactored like product code — never accumulated
as copy-paste arrange blocks. The canonical shape is a scenario, context, and builder,
sized to need:

- **Inline scenarios are the default for small, one-off variations.** The shared scenario
  mechanism owns assembly (a fresh fake filesystem, rendering, and the context); the test
  supplies only a domain-aware builder action. A one-line variation does not earn a class
  and file:

  ```csharp
  var context = SpecScenario.Define(spec =>
      spec.WithSchema("ToolResult", schema => schema.Unrestricted()))
      .Build();
  var document = ingestion.Project(context.SpecPath);
  ```

- **Named scenario classes are promoted, not automatic.** A `sealed` scenario class earns
  one-concept/one-file status when at least one of these holds: the same arrangement is
  reused across test classes; setup is non-trivial (roughly more than five fluent
  statements, or an embedded fixture plus additional shaping); or the situation carries
  durable domain/landmark identity used across slices. The promoted shape:

  ```csharp
  internal sealed class ConfigPluginTupleScenario : SpecScenario
  {
      protected override void Arrange(SpecDocumentBuilder spec) =>
          spec.WithRawSchema("Config", "config-plugin-tuple.json");
  }

  var context = new ConfigPluginTupleScenario().Build();
  ```

  (Type names illustrative — the concrete infrastructure is named at slice planning.)
- **Domain-aware fluent builders.** Scenario state is expressed in domain verbs
  (`WithSchema(...)`, `WithOperation(...)`, `WithResponse(...)`), not raw file writes
  scattered through test bodies. Builders live in the owning test project's `Support/`
  area, compose over the canonical filesystem fake, and follow coding-style rules
  (sealed, no tuple returns, no concrete-collection parameters).
- **Grow on demand.** The scenario mechanism, context, and builder are the pattern; named
  scenarios, test hosts, and builder vocabulary grow only when a promotion/reuse signal
  fires. No speculative infrastructure, and no infrastructure duplication either: the
  second copy-paste of an arrange block first grows a reusable builder verb or preset;
  class promotion follows the rule above rather than file-count convention.

## 2. Filesystem rule

**Testably supplies the repository's only filesystem seam** through the shared
`System.IO.Abstractions.IFileSystem` contract (sealed decision; the independent TestableIO
analyzer enforces it repo-wide — even test code reaches `Path` through `IFileSystem.Path`):

- Levels 1–2 (unit, contract): `Testably.Abstractions.Testing.MockFileSystem`, assembled
  through the scenario builders.
- Level 3 and full-artifact smoke tests: `Testably.Abstractions.RealFileSystem`.
- Raw `System.IO` never appears in test code, and no second filesystem fake is ever
  introduced — one canonical fake per repository.

## 3. Test data policy — no inline dumps

**Raw JSON/XML/string-literal dumps pasted inline in test methods are forbidden.** A
test body states *intent*; the data it runs on lives in one of three sanctioned homes:

1. **Embedded fixture files** — under the owning test project's `Fixtures/` folder,
   loaded through a resource-loader helper by name. The default for wire-shaped data:
   one small file per quirk, named for the construct it isolates, reviewable on its own.
2. **Typed builders** — for variation families where files would multiply: red tests
   composing a valid base plus exactly one offending construct through the builder. The
   variation reads as a domain statement in the test body, not as a diff between two
   pasted strings.
3. **Centralized constants** — small, single-file-scoped values in a static
   `<Domain>Data` class. Never the same literal repeated across test methods.

A short literal is acceptable only when the literal *is* the subject under test (a
media-type string in a media-type parsing test). Data that describes structure always
goes through 1 or 2.

Representative wire fixtures prove runtime behavior, not exhaustive union membership. Do not
invent one hand-authored payload per branch and call that contract breadth: converter maps,
serializer registration, and plural membership are checked mechanically through the bound and
emitted plans, while a small schema-valid runtime corpus exercises framing and deserialization.
Real observed frames are promoted into that corpus when available. Documentation states the
observed fixture count rather than projecting structural completeness onto it.

Runtime tests assert transport/framing, JSON materialization, required .NET shape, and union
dispatch. They do not mutate otherwise representable payloads solely to prove that the SDK
revalidates an OpenAPI range, fixed literal, optional-null distinction, or collection child
constraint; those remain server responsibilities (ADR-0014). Generator tests still fail closed on
unsupported OpenAPI constructs and prove the exact required/nullable C# mapping.

## 4. Fakes and mocks

- **Substitute at seams only** (NSubstitute over interfaces). Records, IR types, and
  pure classes are constructed, never mocked.
- **Never hand-build types you do not own** where a published contract can be exercised
  instead: ingestion fixtures load through the pinned reader — its DOM types are never
  constructed by hand in tests. This is the authorship side of the sealed
  no-mock-framework and fake-only-published-contracts principles.
- **No giant shared `TestBase`.** Shared behavior lives in scenario bases and small
  single-responsibility helpers; a base class accumulating unrelated conveniences is
  split like any other class (§1 red flags apply to test code).
- **If mocking hurts, fix the seam, not the test.** A painful mock setup means the
  production boundary is wrong — redesign the seam instead of layering test helpers
  over it.

## 5. TUnit mechanics

- Naming: `{Symbol}_Should_{Expected_Behavior}[_When_{Condition}]` (`AGENTS.md`); test
  classes `{Sut}Tests`, one class per file; promote a SUT to a folder with per-area
  test files only when it outgrows comfortable navigation.
- TUnit creates a fresh instance per test: setup belongs in the constructor or
  `[Before(Test)]`; no state carried between tests, no shared mutable fields.
- Assertions state intent in the test body — snapshot testing is reserved for its two
  sealed uses (emitter micro-snapshots and the public API surface lock); behavior tests
  never snapshot.

## 6. Anti-patterns (never)

- Inline data dumps (§3), or the same literal appearing in two test methods.
- Real file I/O in a level-1/2 test; raw `System.IO` anywhere in tests.
- Full-spec count assertions against the pin — counts are research-doc facts, and a
  count test turns every legitimate spec refresh into noise.
- `Skip` outside the sanctioned mechanisms (the container conditional skip and
  `[Quarantined]`).
- A second filesystem fake, a resurrected retired helper, or infrastructure kept "just
  in case" — deleted code is recoverable from git.
- Testing library internals (the OpenAPI reader's conformance, TUnit itself, the BCL)
  — test our rules at our boundaries.
