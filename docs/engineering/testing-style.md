# Testing Style — authoring tests

Date: 2026-08-11

Binding authorship style for every test in this repository. The testing *architecture* —
levels, projects, the dual-mode harness, coverage gates, CI — is owned by the sealed
testing design (`AGENTS.md` testing posture; fine-grained design currently in the
transient design specs, reachable via `docs/ROADMAP.md`, distilled at build-out); this
document governs how tests are *written* inside that architecture, and it applies
`coding-style.md` to test code — test infrastructure is code and follows every rule
there.

## 1. Test infrastructure is a first-class citizen

Test setup is designed, reviewed, and refactored like product code — never accumulated
as copy-paste arrange blocks. The canonical shape is a trio, sized to need:

- **Named scenario classes.** A non-trivial setup is a `sealed` scenario class whose
  name states the situation, deriving from a small scenario base that owns assembly
  mechanics and returns a context object carrying exactly what the SUT needs
  (filesystem, input paths, configuration). One scenario, one concept, one file:

  ```csharp
  internal sealed class UnrestrictedSchemaScenario : SpecScenarioBase
  {
      protected override void Arrange(SpecDocumentBuilder spec) =>
          spec.WithSchema("ToolResult", schema => schema.Unrestricted());
  }

  // in a test:
  var context = new UnrestrictedSchemaScenario().Build();   // filesystem + spec path
  var document = ingestion.Project(context.SpecPath);
  ```

  (Type names illustrative — the concrete infrastructure is named at slice planning.)
- **Domain-aware fluent builders.** Scenario state is expressed in domain verbs
  (`WithSchema(...)`, `WithOperation(...)`, `WithResponse(...)`), not raw file writes
  scattered through test bodies. Builders live in the owning test project's `Support/`
  area, compose over the canonical filesystem fake, and follow coding-style rules
  (sealed, no tuple returns, no concrete-collection parameters).
- **Grow on demand.** The trio — scenario base, context, builder — is the pattern; the
  scenario catalog and builder vocabulary grow with the slices that need them. No
  speculative infrastructure, and no infrastructure duplication either: the second
  copy-paste of an arrange block is the signal to grow the builder.

## 2. Filesystem rule

**TestableIO is the repository's only filesystem seam** (sealed decision; the TestableIO
analyzer enforces it repo-wide — even test code reaches `Path` through
`IFileSystem.Path`):

- Levels 1–2 (unit, contract): `MockFileSystem` (`TestingHelpers`), assembled through
  the scenario builders.
- Level 3 and full-artifact smoke tests: the real `FileSystem` (`Wrappers`).
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
- Assertions state intent in the test body — snapshot testing is reserved for its three
  sealed uses (emitter micro-snapshots, the ingestion SpecIR-of-the-pin snapshot, the
  public API surface lock); behavior tests never snapshot.

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
