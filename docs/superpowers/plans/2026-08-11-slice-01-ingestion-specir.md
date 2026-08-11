# Slice 1 — Ingestion + SpecIR Implementation Plan

Date: 2026-08-11

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development
> (recommended) or deniz-process:executing-plans to implement this plan task-by-task. Steps
> use checkbox (`- [ ]`) syntax for tracking.

**Goal:** stand up the tooling foundation (full-battery DI composition, MEL logging,
global CLI interception, Testably filesystem seam, first-class test infrastructure) and
build the fail-closed ingestion stage on it — the pinned Microsoft.OpenApi
reader, the per-host whitelist dialect wall, the semantic projection into the minimal
immutable SpecIR, raw-content hashes, library-upgrade tripwires, DOM-boundary guards, and the
full pinned-spec landmark smoke test (generator spec §4.1).

**Architecture:** `ISpecIngestion` (the Binder-facing seam) orchestrates three stages inside
`tools/OpenCode.Sdk.Tools/Generator/Ingestion/`: `SpecReader` (loads the document through
`IFileSystem` + `OpenApiDocument.LoadAsync`, enforces the version gate, promotes reader
diagnostics to errors, translates reader exceptions), the per-host wall policies (whitelist
tables over every consumed DOM type), and the projectors (schema classification,
normalizations, operation surface, graph keys, raw-content hashes) producing `SpecDocument`.
Everything follows `docs/engineering/coding-style.md` (named collaborators, full CLI
composition, seams + DI, signature hygiene) and `docs/engineering/testing-style.md`
(lambda-first one-off scenarios, promoted named scenarios, domain builders, embedded fixtures —
no inline JSON dumps). The mutable Microsoft.OpenApi DOM never escapes
`Generator/Ingestion/`.

**Tech Stack:** Microsoft.OpenApi 3.9.0 (pinned; tooling-only), Testably.Abstractions 10.3.0,
Testably.Abstractions.Testing 7.0.2, and the independent
`TestableIO.System.IO.Abstractions.Analyzers` 2022.0.0 (repo-wide), Spectre.Console +
Spectre.Console.Cli + DI registrar (already pinned), Microsoft.Extensions.Logging with
Spectre and optional Testably-backed file providers, System.Text.Json (in-box), TUnit on
MTP (pinned), Verify.TUnit (new pin — the SpecIR-of-the-pin snapshot).

## Global Constraints

- `LangVersion=14.0`, `AnalysisLevel=10.0` — deliberate numeric pins; never "fix" to
  `latest` (AGENTS.md Hard Rules).
- Full analyzer wall + `TreatWarningsAsErrors=true` applies to `tools/`: CA1062 guards
  (`ArgumentNullException.ThrowIfNull` / `ArgumentException.ThrowIfNullOrWhiteSpace`),
  MA0048 one type per file, IDE0130 folder = namespace, MA0051 method length,
  culture-invariant formatting (`FormattableString.Invariant` / `CultureInfo.InvariantCulture`),
  `StringComparison.Ordinal` everywhere. Async code carries the `ConfigureAwait(false)`
  triple (product code; tests exempt).
- **`docs/engineering/coding-style.md` and `docs/engineering/testing-style.md` are binding
  constraints of every task** — named collaborators over private-method accumulation;
  interfaces at seams only, `sealed` elsewhere; no tuple returns or concrete-collection
  parameters across class boundaries; vertical layout with `Abstractions/`+`Models/` groups;
  centralized scenario assembly + domain builders + embedded fixtures; **no inline JSON
  dumps in test bodies**. Violations are defects, not preferences.
- **Scenario granularity:** small one-off variations use `SpecScenario.Define(...)` or
  `SpecScenario.FromRawJson(...)` inline. A named scenario class is promoted only when the
  arrangement is reused across test classes, is non-trivial (roughly more than five fluent
  statements or fixture-plus-shaping), or carries durable cross-slice domain/landmark
  identity. Every inventory case remains a full test; only its setup housing changes.
- Analyzer misfires: per-rule arbitration with a winner-naming comment — never a rollback,
  never an inline suppression without arbitration.
- Test naming: `{Symbol}_Should_{Expected_Behavior}[_When_{Condition}]`; classes
  `{Sut}Tests`, one per file. TUnit syntax: `[Test]`,
  `await Assert.That(x).IsEqualTo(y)`; exception capture:
  `var ex = await Assert.That(() => ...).Throws<IngestionException>();` then assert on
  `ex!.Message`. Adapt in place if the pinned TUnit differs — level-0 deviation.
- CPM: new pins are `Microsoft.Extensions.Logging`, `Spectre.Console`,
  `Microsoft.OpenApi` 3.9.0, `Testably.Abstractions` 10.3.0,
  `Testably.Abstractions.Testing` 7.0.2,
  `TestableIO.System.IO.Abstractions.Analyzers` 2022.0.0, and `Verify.TUnit` (newest stable).
  Before pinning, re-check with `dotnet package search <id> --exact-match` and pin the newer
  stable if one exists. If the ingestion behavior differs on a newer Microsoft.OpenApi,
  stop — level 2 (the spec's evidence is 3.9.0).
- No count assertions against the pinned full spec — shape and named-landmark assertions
  only.
- Determinism: schema-graph keys ordinal-sorted; operation list and member order are
  document order; no wall-clock, no randomness.
- Defensive programming default: unknown constructs refuse with located, batched errors;
  silent fallbacks are forbidden outside the recorded tolerances (known-ignored validation
  keywords, the opaque `x-effect-stream`, the admitted `prefixItems` site, library
  bookkeeping members).
- Everything temporary goes to `.scratchpad/` (gitignored).
- After every task (before its commit): run
  `dotnet tool run slopwatch analyze --exclude ".scratchpad/**,external/**" --fail-on warning`.
- **The full gate** (every task) = `dotnet build --configuration Release` →
  `dotnet test --configuration Release --no-build` →
  `dotnet format --verify-no-changes --no-restore` → the Slopwatch command. All four clean.
- Conventional Commits; per-task commits on the slice branch are the agreed loop; master
  merges via PR only.
- Contradictions with a sealed spec: stop and classify per
  `docs/agents/deviation-protocol.md`. Subagents never self-resolve level 2+.
- Work happens on branch `feature/slice-01-ingestion-specir` in a worktree
  (deniz-process:using-git-worktrees).
- Out of scope (hidden-scope ban): Binder, curation, emitters, Writer, generated output,
  `generate` pipeline wiring beyond DI registration, fingerprint *persistence* (§9 composing
  is Binder work — this slice only computes and carries the raw hashes), CI workflow changes.

## Reference implementations (adapt, never transplant)

Run-proven reference code exists for nearly every mechanism in this plan — the redesign
probes (five modes plus the grill's `members`/`hostbisect`) and the retired parser on the
`feature/slice-01-parser-specir` evidence branch (its `SpecMediaType`, error collector,
graph-key mechanics and test-case inventories adapt well; its architecture is the
counter-example — research log session 12). Exact locations and a per-task map live in the
execution handover (`docs/agents/handover-prompts/HANDOFF-2026-08-11.md`). Rules: probes
are blacklist-shaped single files — production is whitelist-shaped collaborators under
`coding-style.md`; adapt the mechanics, never copy the shape; never modify the evidence
worktree.

## SpecIR at a glance (locked reference — each task restates what it needs)

All ingestion types live under `tools/OpenCode.Sdk.Tools/Generator/Ingestion/`, namespace
`OpenCode.Sdk.Tools.Generator.Ingestion[.Models|.Abstractions|.Walls|.Projection]`, one file
per type, records immutable (`required`/`init`, `IReadOnly*` frozen copies).

| Type | Role |
|---|---|
| `ISpecIngestion` | the seam: `Task<SpecDocument> IngestAsync(string specPath, CancellationToken ct)` |
| `IngestionException` | batched refusal; `IReadOnlyList<IngestionError> Errors` |
| `IngestionError` | `record IngestionError(string Location, string Problem)` |
| `SpecDocument` | `Operations` (document order), `Schemas` (ordinal-sorted keys), `SchemaContentHashes` (ordinal-sorted; name → SHA-256 hex) |
| `SpecOperation` | id, surface, method, path, wildcard/websocket/SSE/deprecated flags, summary/description, parameters, request body, responses (status-ascending; each carries envelope shape, SSE flag and the opaque `EffectStreamJson`), `RawContentHash` |
| `SpecParameter`, `SpecRequestBody`, `SpecResponse`, `SpecMediaType` | operation surface details |
| `SpecSurface`, `SpecParameterLocation`, `SpecEnvelopeShape` | operation-side enums |
| `SchemaNode` (abstract: `Description`, `Format`, `Children`) | graph node base |
| `PrimitiveNode`, `EnumNode`, `LiteralNode`, `ObjectNode` (+`SpecProperty`; hybrid carries `AdditionalPropertiesSchema`), `DictionaryNode`, `FreeFormObjectNode`, **`UnrestrictedNode`**, `ArrayNode`, `TupleNode`, `UnionNode` (+`UnionClassification` Marked/Structural), `NullableNode`, `RefNode`, `SpecialNumberNode`, `JsonStringNode` | node kinds |
| `LiteralMarker`, `ErrorStyle`, `LiteralDialect`, `UnionKeyword`, `AdditionalPropertiesKind`, `PrimitiveKind`, `LiteralKind` | node-side facts |

**Graph keys (locked):** named schemas use the wire name verbatim (`Session`,
`session.status`). Promoted inline types use `{root}#{pointer}`: root = owning named schema's
wire name or `op:{operationId}`; pointer segments `/properties/{name}`, `/items`,
`/additionalProperties`, `/patternProperties`, `/prefixItems/{index}`, `/contentSchema`,
`/anyOf/{branch}`, `/oneOf/{branch}`, `/parameters/{name}`, `/requestBody`,
`/responses/{status}`. A union-branch `{branch}` is `{prop}={value}` from the branch's
alphabetically-first literal marker; **branches without a marker use the ordinal index**.
Segment names JSON-pointer-escape `~` → `~0` and `/` → `~1`. Key collision ⇒ error. Never a
document-global counter.

**The wall (locked; generator spec §4.1's per-host tables are the authority):** any populated
*spec-derived* member outside a host's admitted/known-ignored table refuses with a located
error; library bookkeeping members (`BaseUri`, `Self`, `Workspace`, `Metadata`) are exempt
from the wall but covered by the tripwire snapshots. Reader rules: `SpecificationVersion`
must be `OpenApi3_1`; any reader diagnostic error fails ingestion; reader exceptions
translate to located errors. `UnrecognizedKeywords` must be empty except `prefixItems` under
`Config.plugin`. Extension dispositions: `x-codeSamples` ignored (operations), `x-websocket`
flag (operations), `x-effect-stream` opaque (SSE media only), all other `x-*` refuse.

---

### Task 1: Package pins + full ToolApp composition root

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `Directory.Build.props` (TestableIO analyzer joins the repo-wide analyzer wall)
- Modify: `tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj`
- Modify: `tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj`
- Modify: `tools/OpenCode.Sdk.Tools/ToolApp.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Commands/GenerateCommand.cs`
- Create: `tools/OpenCode.Sdk.Tools/GlobalSettings.cs`
- Create: `tools/OpenCode.Sdk.Tools/Infrastructure/GlobalOptionsInterceptor.cs`
- Create: `tools/OpenCode.Sdk.Tools/Infrastructure/Logging/ToolLoggingOptions.cs`,
  `SpectreConsoleLoggerProvider.cs`, `FileLoggerProvider.cs`, `ToolLogLevelConverter.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/ToolAppTests.cs` (extend the existing class),
  `tests/OpenCode.Sdk.Tools.Tests/Infrastructure/Logging/ToolLoggingTests.cs`

**Interfaces:**
- Consumes: slice 0's `ToolApp` factory seam (`CreateRegistrar`/`Configure`) and the
  existing `CommandAppTester` harness.
- Produces the complete CLI hosting/DI topology (coding-style §2), not a skeleton:
  - `ToolApp.CreateServices(Action<IServiceCollection>? overrideServices = null)` is the
    single registration root. Defaults are registered first; the optional callback runs
    last for seam replacement. `CreateRegistrar` retains its existing optional override
    callback and delegates to this method. Tests never reproduce the registration list.
  - Core seams: singleton `IFileSystem` → `RealFileSystem` and `IAnsiConsole` →
    `AnsiConsole.Console`; later tasks extend this same root with `ISpecIngestion`, Binder,
    emitters, Writer, and infrastructure wrappers.
  - MEL end to end: commands/services consume `ILogger<T>`; a Spectre provider writes
    through `IAnsiConsole`; an optional file provider writes only through `IFileSystem`.
    Both providers read the singleton `ToolLoggingOptions` applied per invocation.
  - `GlobalSettings : CommandSettings` carries global `--log-level` and `--log-file`.
    Accepted levels match the tooling UX (`trace`, `debug`, `info`, `warning`, `error`,
    `none`), default `warning`; `ToolLogLevelConverter` maps them to MEL. Every command's
    settings derive from this base.
  - `GlobalOptionsInterceptor : ICommandInterceptor` maps `GlobalSettings` into
    `ToolLoggingOptions` before command execution. It is DI-registered; do not use the
    obsolete single-interceptor setter.
  - `GenerateCommand.Settings : GlobalSettings` is its nested concrete settings type;
    `GenerateCommand` injects `ILogger<GenerateCommand>` alongside `IAnsiConsole`, logs its
    invocation, and remains the same nonzero fail-loud stub. Future command settings derive
    from the same global base.

- [x] **Step 1: Add the package pins.** In `Directory.Packages.props` (re-check newest
  stable first — Global Constraints): add `Microsoft.Extensions.Logging` beside the
  existing DI package; add the direct `Spectre.Console` dependency beside its CLI packages;
  under third-party analyzers add `TestableIO.System.IO.Abstractions.Analyzers` `2022.0.0`;
  under third-party packages add `Microsoft.OpenApi` `3.9.0` and
  `Testably.Abstractions` `10.3.0`; under test packages add
  `Testably.Abstractions.Testing` `7.0.2` and `Verify.TUnit` (newest stable).
  In `Directory.Build.props`, append the analyzer to the repo-wide analyzer ItemGroup with
  the same `PrivateAssets`/`IncludeAssets` shape as its siblings. (The analyzer is an
  older-Roslyn build; if the compiler refuses to load it, stop — level 2.)
- [x] **Step 2: Reference packages.** Tools csproj adds `Microsoft.Extensions.Logging`,
  `Spectre.Console`, `Microsoft.OpenApi`, and `Testably.Abstractions`; tests csproj adds
  `Testably.Abstractions.Testing` and `Verify.TUnit`.
- [x] **Step 3: Write the failing tests:**
  - `CreateServices_Should_Resolve_Full_Hosting_Composition` builds `CreateServices()` and resolves
    the concrete filesystem, `IAnsiConsole`, `ILogger<GenerateCommand>`,
    `ToolLoggingOptions`, and `IEnumerable<ICommandInterceptor>` containing exactly the
    global interceptor.
  - `CreateRegistrar_Should_Apply_Seam_Overrides_After_Production_Registrations` replaces
    `IFileSystem` with one `MockFileSystem` through the callback and proves the resolved
    singleton is that instance — no test-only service-list reconstruction.
  - `RunAsync_Should_Apply_Global_Logging_Options_Before_Command_Execution` runs
    `generate --log-level debug --log-file logs/tool.log` through `CommandAppTester` with
    MockFileSystem/console seam replacements; the existing nonzero stub result remains,
    the file is created through the fake, and its content includes the command log.
  - Focused provider tests prove default-warning filtering, Spectre markup-safe output,
    and fail-loud behavior when an explicitly configured log file cannot be initialized.
- [x] **Step 4: Run to verify they fail** — missing composition/settings/provider types and
  `ToolApp.CreateServices` are the red state.
- [x] **Step 5: Implement** the Produces block. Provider logic is split by destination;
  the interceptor owns invocation configuration; `ToolApp` owns registration and command
  wiring only. All file paths and writes ride `IFileSystem`; no custom logger facade and no
  silent logging-I/O fallback.
- [x] **Step 6: Run to verify pass** — full suite green (slice-0 command behavior retained).
- [x] **Step 7: Full gate** (all four commands clean). Expect the new IO analyzer to fire
  wherever slice-0 code touched `File`/`Path` directly; fix by routing through the injected
  `IFileSystem` — that is the analyzer doing its job, not a misfire.
- [x] **Step 8: Commit** — `feat(tools): full hosting composition and ingestion package pins`

---

### Task 2: Test infrastructure — scenarios, builders, embedded fixtures

**Files:**
- Create: `tests/OpenCode.Sdk.Tools.Tests/Support/SpecScenario.cs`,
  `Support/ScenarioContext.cs`, `Support/SpecDocumentBuilder.cs`, `Support/SchemaBuilder.cs`,
  `Support/OperationBuilder.cs`, `Support/FixtureLoader.cs`
- Modify: `tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj` (embed
  `Fixtures/**/*.json`; link `spec/openapi.json` as `Fixtures/openapi.json`,
  `CopyToOutputDirectory="PreserveNewest"`)
- Test: `tests/OpenCode.Sdk.Tools.Tests/Support/SpecDocumentBuilderTests.cs`,
  `Support/SpecScenarioTests.cs`, `Support/FixtureLoaderTests.cs`

**Interfaces:**
- Consumes: `Testably.Abstractions.Testing.MockFileSystem`.
- Produces the centralized testing-style §1 shape:
  - `SpecDocumentBuilder` — sealed, fluent, JSON-producing:
    `WithOpenApiVersion(string version = "3.1.0")`,
    `WithSchema(string name, Action<SchemaBuilder> configure)`,
    `WithRawSchema(string name, string fixtureName)` (embedded-fixture body),
    `WithOperation(string operationId, string method = "get", string path = "/api/x", Action<OperationBuilder>? configure = null)`,
    `WithRawTopLevel(string key, string rawJson)` (short subject literals),
    `WithRawTopLevelFromFixture(string key, string fixtureName)` (structural wall fixtures),
    `string BuildJson()`. The builder receives one `FixtureLoader` collaborator; scenario
    assembly supplies it centrally.
  - `SchemaBuilder` — domain verbs used across this plan: `Type(string)`,
    `Property(string name, Action<SchemaBuilder>, bool required = false)`,
    `Required(params string[])`, `Enum(params string[])`, `Const(string)`,
    `AnyOf(params Action<SchemaBuilder>[])`, `OneOf(...)`, `AllOf(...)`, `Ref(string target)`,
    `Items(Action<SchemaBuilder>)`, `PrefixItems(params Action<SchemaBuilder>[])`,
    `AdditionalProperties(Action<SchemaBuilder>)`, `AdditionalPropertiesFalse()`,
    `PatternProperties(string pattern, Action<SchemaBuilder>)`,
    `ContentSchema(string mediaType, Action<SchemaBuilder>)`, `Unrestricted()`,
    `Raw(string key, string rawJson)` (unknown-keyword injection), `Description(string)`,
    `Format(string)`.
  - `OperationBuilder` — `Parameter(string name, string location, Action<SchemaBuilder>, bool required = false, bool deepObject = false)`,
    `RequestBody(string mediaType, Action<SchemaBuilder>, bool required = false)`,
    `Response(int status, string? mediaType = null, Action<SchemaBuilder>? schema = null)`,
    `SseResponse(Action<SchemaBuilder> schema, string? effectStreamJson = null)`,
    `Extension(string key, string rawJson)`, `Raw(string key, string rawJson)`,
    `Deprecated()`, `Summary(string)`.
  - `SpecScenario` — the common scenario mechanism. `Define(Action<SpecDocumentBuilder>)`
    creates the default one-off arranged scenario; `FromRawJson(string)` creates a scenario
    whose literal payload is itself the test subject; `Build()` writes to
    `spec/openapi.json` on a fresh `MockFileSystem` and returns the context. A promoted named
    scenario derives from this same type and overrides its arrangement hook — there is no
    second base/factory hierarchy.
  - `ScenarioContext` —
    `record ScenarioContext(IFileSystem FileSystem, string SpecPath)`.
  - `FixtureLoader` — a sealed instance collaborator with `string Load(string name)` from embedded resources
    (`OpenCode.Sdk.Tools.Tests.Fixtures.{name}`), throwing with the known-names list on a
    miss.

The promotion rule in Global Constraints is binding throughout Tasks 3–7. A one-verb wall
case stays inline; the test remains individually named and asserted. Reusable builder verbs
or presets are extracted at the second repeated arrangement; named scenario classes appear
only when their stronger promotion rule fires.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Test]
public async Task BuildJson_Should_Produce_Document_With_Schema_And_Operation()
{
    var json = new SpecDocumentBuilder()
        .WithSchema("Session", s => s.Type("object")
            .Property("id", p => p.Type("string"), required: true)
            .AdditionalPropertiesFalse())
        .WithOperation("v2.session.get", method: "get", path: "/api/session/{sessionID}",
            configure: op => op
                .Parameter("sessionID", "path", p => p.Type("string"), required: true)
                .Response(200, "application/json", s => s.Ref("Session")))
        .BuildJson();

    var root = JsonNode.Parse(json)!;
    await Assert.That(root["openapi"]!.GetValue<string>()).IsEqualTo("3.1.0");
    await Assert.That(root["components"]!["schemas"]!["Session"]!["required"]!.AsArray()).HasCount(1);
    await Assert.That(root["paths"]!["/api/session/{sessionID}"]!["get"]!["operationId"]!
        .GetValue<string>()).IsEqualTo("v2.session.get");
}

[Test]
public async Task Build_Should_Write_Spec_To_Mock_FileSystem()
{
    var context = SpecScenario.Define(_ => { }).Build();

    await Assert.That(context.FileSystem.File.Exists(context.SpecPath)).IsTrue();
}

[Test]
public async Task FromRawJson_Should_Write_The_Subject_Payload_Unchanged()
{
    var context = SpecScenario.FromRawJson("{ not json").Build();

    await Assert.That(context.FileSystem.File.ReadAllText(context.SpecPath))
        .IsEqualTo("{ not json");
}

[Test]
public async Task Load_Should_Throw_With_Known_Names_When_Fixture_Missing()
{
    var loader = new FixtureLoader();

    var ex = await Assert.That(() => loader.Load("no-such-fixture"))
        .Throws<ArgumentException>();
    await Assert.That(ex!.Message).Contains("no-such-fixture");
}
```

- [ ] **Step 2: Run to verify they fail** (CS0246 for the new types).
- [ ] **Step 3: Implement** the six Support types. Builders compose `JsonObject` trees
  internally (never string concatenation); coding-style applies (sealed concrete helpers,
  no tuples, no concrete-collection parameters on non-private members). The private
  implementations behind `SpecScenario.Define`/`FromRawJson` stay inside the scenario
  mechanism rather than creating one top-level type/file per test variation.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Full gate.**
- [ ] **Step 6: Commit** — `test(tools): centralized spec scenarios, builders and fixtures`

---

### Task 3: Reader gate — `SpecReader`, errors, exception translation

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Ingestion/Models/IngestionError.cs`,
  `Models/IngestionException.cs`
- Create: `tools/OpenCode.Sdk.Tools/Generator/Ingestion/IngestionErrorCollector.cs` (internal)
- Create: `tools/OpenCode.Sdk.Tools/Generator/Ingestion/SpecReader.cs` (internal sealed)
- Create: `tools/OpenCode.Sdk.Tools/Generator/Ingestion/LoadedSpec.cs` (internal record)
- Create: `tests/OpenCode.Sdk.Tools.Tests/Fixtures/boolean-property-schema.json`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecReaderTests.cs`

**Interfaces:**
- Consumes: `IFileSystem` (Task 1 DI), `SpecScenario` and the domain builders (Task 2).
- Produces:
  - `public sealed record IngestionError(string Location, string Problem);`
  - `public sealed class IngestionException : Exception` —
    `IReadOnlyList<IngestionError> Errors` + the three standard constructors (empty
    `Errors`); message = `Ingestion failed with {n} error(s):` + one line per error.
  - `internal sealed class IngestionErrorCollector` — `void Add(string location, string problem)`,
    `bool HasErrors`, `void ThrowIfAny()`.
  - `internal sealed record LoadedSpec(OpenApiDocument Document, JsonNode Raw);`
  - `internal sealed class SpecReader(IFileSystem fileSystem)` —
    `Task<LoadedSpec> LoadAsync(string specPath, IngestionErrorCollector errors, CancellationToken ct)`:
    missing file → error; opens the stream through `IFileSystem`, calls
    `OpenApiDocument.LoadAsync(stream, "json", settings)` with
    `OpenApiReaderSettings { LeaveStreamOpen = true }` inside try/catch — **any** reader
    exception becomes `document: the reader failed — {type}: {message}` (the boolean-schema
    NRE class); rewinds and parses the same stream into `JsonNode` (the raw side for hashes
    and sibling scans); every `result.Diagnostic` error becomes an `IngestionError`
    (`Location` = the diagnostic pointer when non-empty, else `document`; the message text
    carries the position for this reader); `SpecificationVersion != OpenApi3_1` → error.
    On any error: `ThrowIfAny` before returning.

- [ ] **Step 1: Write the failing tests** (each red case is a builder/scenario expression —
  no inline JSON):

```csharp
public sealed class SpecReaderTests
{
    private static async Task<IngestionException> LoadExpectingRefusal(SpecScenario scenario)
    {
        var context = scenario.Build();
        var reader = new SpecReader(context.FileSystem);
        var errors = new IngestionErrorCollector();
        var ex = await Assert.That(() => reader.LoadAsync(context.SpecPath, errors, CancellationToken.None))
            .Throws<IngestionException>();
        return ex!;
    }

    [Test]
    public async Task LoadAsync_Should_Return_Document_And_Raw_For_Valid_31_Document()
    {
        var context = SpecScenario.Define(_ => { }).Build();
        var reader = new SpecReader(context.FileSystem);

        var loaded = await reader.LoadAsync(context.SpecPath, new IngestionErrorCollector(), CancellationToken.None);

        await Assert.That(loaded.Document.Paths).IsNotNull();
        await Assert.That(loaded.Raw["openapi"]!.GetValue<string>()).IsEqualTo("3.1.0");
    }

    [Test]
    public async Task LoadAsync_Should_Refuse_When_Version_Is_Not_31()
    {
        var ex = await LoadExpectingRefusal(
            SpecScenario.Define(spec => spec.WithOpenApiVersion("3.2.0")));
        await Assert.That(ex.Message).Contains("3.2");
    }

    [Test]
    public async Task LoadAsync_Should_Refuse_When_Spec_File_Is_Missing()
    {
        var reader = new SpecReader(new MockFileSystem());
        var ex = await Assert.That(() => reader.LoadAsync("spec/openapi.json", new IngestionErrorCollector(), CancellationToken.None))
            .Throws<IngestionException>();
        await Assert.That(ex!.Message).Contains("spec/openapi.json");
    }

    [Test]
    public async Task LoadAsync_Should_Translate_Reader_Crash_When_Schema_Is_Boolean()
    {
        var ex = await LoadExpectingRefusal(SpecScenario.Define(spec =>
            spec.WithRawSchema("Bad", "boolean-property-schema.json")));
        await Assert.That(ex.Message).Contains("reader failed");
    }

    [Test]
    public async Task LoadAsync_Should_Promote_Reader_Diagnostics_To_Errors()
    {
        var ex = await LoadExpectingRefusal(SpecScenario.Define(spec =>
            spec.WithOperation("v2.test.get", configure: operation =>
                operation.Raw("madeUpKey", "{}"))));
        await Assert.That(ex.Message).Contains("madeUpKey");
    }

    [Test]
    public async Task LoadAsync_Should_Refuse_When_Json_Is_Malformed()
    {
        var ex = await LoadExpectingRefusal(SpecScenario.FromRawJson("{ not json"));
        await Assert.That(ex.Message).Contains("reader failed");
    }
}
```

  These are deliberately inline one-off variations. The boolean-schema payload is an
  embedded fixture because it is structural wire data; malformed JSON uses
  `FromRawJson` because the literal itself is the subject. No scenario class/file is
  created for version, unknown-key, or malformed-input variants.
- [ ] **Step 2: Run to verify they fail** (CS0246).
- [ ] **Step 3: Implement** per the Produces block. Keep `SpecReader` small — it is the
  reader *gate*, not the wall.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Full gate.**
- [ ] **Step 6: Commit** — `feat(tools): spec reader gate with version, diagnostic and crash walls`

---

### Task 4: Schema wall + core schema projection (primitives, enum, array, ref, unrestricted, promotion)

**Files:**
- Create: `Generator/Ingestion/Models/SchemaNode.cs`, `Models/PrimitiveNode.cs`,
  `Models/PrimitiveKind.cs`, `Models/EnumNode.cs`, `Models/ArrayNode.cs`, `Models/RefNode.cs`,
  `Models/UnrestrictedNode.cs`
- Create: `Generator/Ingestion/Walls/SchemaWallPolicy.cs` (internal sealed)
- Create: `Generator/Ingestion/Projection/SchemaProjector.cs` (internal sealed),
  `Projection/GraphKeyBuilder.cs` (internal sealed), `Projection/ProjectionState.cs` (internal)
- Create: `tests/OpenCode.Sdk.Tools.Tests/Support/SchemaProjectionTestHost.cs`,
  `Support/SchemaProjectionResult.cs`
- Test: `tests/.../SchemaWallPolicyTests.cs`, `SchemaProjectorTests.cs`, `GraphKeyBuilderTests.cs`

**Interfaces:**
- Consumes: `SpecReader`/`LoadedSpec` (Task 3), builders (Task 2).
- Produces:
  - `public abstract record SchemaNode { string? Description; string? Format; abstract IEnumerable<SchemaNode> Children; }`
  - `PrimitiveNode { required PrimitiveKind Kind }` (`String|Number|Integer|Boolean`),
    `EnumNode { required IReadOnlyList<string> Values }`,
    `ArrayNode { required SchemaNode Item }`, `RefNode { required string Target }`,
    `UnrestrictedNode` (no members — the any-value node).
  - `internal sealed class SchemaWallPolicy` —
    `void Check(OpenApiSchema schema, string location, IngestionErrorCollector errors)`:
    the §4.1 schema table rendered as code. Refuses populated `AllOf` (outside union
    analysis — Task 6 consumes it first), multi-flag `Type`, `Discriminator`, `Not`,
    `If`/`Then`/`Else`, `DependentSchemas`/`DependentRequired`, `PropertyNames`, `Contains`,
    `UnevaluatedProperties == false`, `$defs`/dynamic members, `Title`, `Default`,
    `Examples`/`Example`, `ReadOnly`/`WriteOnly`, `Xml`, `ExternalDocs`,
    `MinLength`/`MaxLength`, `MultipleOf`, `MinProperties`/`MaxProperties`, `UniqueItems`;
    known-ignored: `pattern`, `minimum`, `maximum`, `exclusiveMinimum`, `minItems`,
    `maxItems`; `UnrecognizedKeywords` non-empty → error unless the admitted site
    (`prefixItems` at `Config/properties/plugin/items/anyOf/1` — Task 6 consumes it);
    schema-level `Extensions` → error. Bookkeeping members (`Metadata`) exempt.
  - `internal sealed class GraphKeyBuilder` — `string Root(string wireNameOrOpId)`,
    `string Append(string parentPointer, string segment)` (escapes `~`→`~0`, `/`→`~1`).
  - `internal sealed class SchemaProjector(SchemaWallPolicy wall, GraphKeyBuilder keys)` —
    `SchemaNode? Project(IOpenApiSchema schema, string root, string pointer, ProjectionState state)`
    where `ProjectionState` (internal) carries the graph dictionary + error collector +
    visited identity set. Reference wrappers → `RefNode` via typed
    `OpenApiSchemaReference.Reference.Id` (never proxy-walked; unresolved `Target` →
    error); `$ref` siblings detected on the **raw side** in Task 8's orchestration (state
    carries the raw pointer lookup) — this task records the rule; concrete sibling check
    lands with the raw walk in Task 8. Dispatch (this task): unrestricted (no admitted
    constraint member populated; annotations allowed) → `UnrestrictedNode`; scalar `Type`
    → `PrimitiveNode` (+`Format` recorded); `string`+multi-value `Enum` → `EnumNode`;
    `array`+`Items` → `ArrayNode`; array without items → error; promotion: inline
    `EnumNode` (this task; `ObjectNode`/`UnionNode` in Tasks 5–6) registers under
    `{root}#{pointer}` and returns a `RefNode`; key collision → error.

- [ ] **Step 1: Write the failing tests.** Exemplars in full; every listed case becomes a
  real `[Test]` following the same pattern. `SchemaProjectionTestHost` is the reusable
  named host for Tasks 4–6: it takes a `SpecScenario`, runs the real reader + projector,
  and returns `SchemaProjectionResult` (schemas + optional refusal) — never a tuple. One-off
  arrangements stay inline:

```csharp
[Test]
public async Task Project_Should_Produce_Unrestricted_Node_For_Empty_Schema()
{
    var host = new SchemaProjectionTestHost();
    var scenario = SpecScenario.Define(spec =>
        spec.WithSchema("ToolResult", schema => schema.Unrestricted()));

    var result = await host.ProjectAsync(scenario);

    await Assert.That(result.Schemas["ToolResult"]).IsTypeOf<UnrestrictedNode>();
}

[Test]
public async Task Project_Should_Refuse_When_Schema_Uses_AllOf()
{
    var host = new SchemaProjectionTestHost();
    var scenario = SpecScenario.Define(spec =>
        spec.WithSchema("Bad", schema => schema.AllOf(branch => branch.Type("string"))));

    var ex = await host.ProjectExpectingRefusalAsync(scenario);

    await Assert.That(ex.Message).Contains("allOf");
    await Assert.That(ex.Message).Contains("Bad");
}
```

  Case inventory (write each in full): primitives ×4 kinds; `Format` recorded;
  `Description` recorded; description-only schema → `UnrestrictedNode` (annotations
  permitted — the session-13 definition); dotted schema name kept verbatim; multi-value
  enum; array with item; promoted inline enum under `/items` (graph key asserted); ref to
  existing target; refusals: type array, `discriminator`, `if/then`, `title`, `default`,
  `readOnly`, `multipleOf`, `uniqueItems`, unknown raw keyword (via `SchemaBuilder.Raw`),
  schema-level `x-*`, ref target missing, array without items, batched multi-error
  (two bad schemas both named); known-ignored validation keywords parse clean;
  `GraphKeyBuilder` escaping (`a/b` → `a~1b`, `a~b` → `a~0b`). The one-verb variants
  use inline scenarios; no per-keyword scenario files are created.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** per Produces. `SchemaWallPolicy` is a table-driven check, not a
  method chain; `SchemaProjector` dispatch stays a thin switch delegating to small
  factory-per-kind privates only for *narrative* — anything with policy weight is its own
  collaborator (coding-style §1 red flags govern splits from the start).
- [ ] **Step 4: Run to verify pass.** — [ ] **Step 5: Full gate.**
- [ ] **Step 6: Commit** — `feat(tools): schema wall policy and core schema projection`

---

### Task 5: Object family — objects, hybrids, dictionaries, free-form

**Files:**
- Create: `Models/ObjectNode.cs`, `Models/SpecProperty.cs`, `Models/AdditionalPropertiesKind.cs`,
  `Models/DictionaryNode.cs`, `Models/FreeFormObjectNode.cs`
- Modify: `Projection/SchemaProjector.cs`
- Test: `tests/.../SchemaProjectorObjectTests.cs`

**Interfaces:**
- Consumes: Task 4's dispatch + promotion + wall.
- Produces:
  - `SpecProperty { required string Name; required SchemaNode Schema; required bool IsRequired; }`
  - `ObjectNode { required IReadOnlyList<SpecProperty> Properties; required AdditionalPropertiesKind AdditionalProperties; SchemaNode? AdditionalPropertiesSchema; required IReadOnlyList<LiteralMarker> LiteralMarkers; required ErrorStyle ErrorStyle; }`
    — `LiteralMarkers`/`ErrorStyle` arrive in Task 6; this task constructs them empty/None
    at the single construction site. **Hybrid objects** (properties **and** an
    additionalProperties schema — 6 pin sites) populate both `Properties` and
    `AdditionalPropertiesSchema` with `AdditionalProperties == Schema`.
  - `AdditionalPropertiesKind { Open, Forbidden, Schema }`;
    `DictionaryNode { required SchemaNode Value; }`; `FreeFormObjectNode`.
  - Dispatch rules: `properties` present → `ObjectNode` (property names are opaque wire
    data — a property named `type`/`properties`/`required` never keyword-matches; property
    order is document order; `required` names must match properties, else error);
    no properties + AP schema → `DictionaryNode`; no properties + single
    `patternProperties` entry → `DictionaryNode` (pattern dropped as validation-only;
    multiple patterns or combination with properties/AP → error); bare `object` →
    `FreeFormObjectNode`; `additionalProperties: true` explicit → treated as absent
    (`AdditionalPropertiesAllowed` default-true semantics; the tripwire snapshot pins the
    default); inline `ObjectNode` becomes promotion-eligible.

- [ ] **Step 1: Write the failing tests** — exemplar plus inventory (each written in full):

```csharp
[Test]
public async Task Project_Should_Keep_Property_Schema_And_Dictionary_Value_For_Hybrid_Objects()
{
    var host = new SchemaProjectionTestHost();
    var scenario = SpecScenario.Define(spec => spec.WithSchema("ProviderOptions", schema =>
        schema.Type("object")
            .Property("timeout", property => property.Type("number"))
            .AdditionalProperties(value => value.Type("string"))));

    var result = await host.ProjectAsync(scenario);

    var node = (ObjectNode)result.Schemas["ProviderOptions"];
    await Assert.That(node.AdditionalProperties).IsEqualTo(AdditionalPropertiesKind.Schema);
    await Assert.That(node.AdditionalPropertiesSchema).IsNotNull();
    await Assert.That(node.Properties.Single(p => p.Name == "timeout").IsRequired).IsFalse();
}
```

  Inventory: object with required set; property order = document order; promoted inline
  object property (graph key asserted); keyword-named properties as plain data (the
  `GlobalEvent` trap — properties literally named `type`/`properties`/`required`);
  dictionary via AP schema; dictionary via single patternProperties; patternProperties
  multi-pattern refuse; patternProperties+properties refuse; free-form; empty-properties
  object; `required` naming a missing property → located error; `additionalProperties: true`
  → `Open`.
- [ ] **Step 2: verify fail.** — [ ] **Step 3: Implement.** — [ ] **Step 4: verify pass.**
- [ ] **Step 5: Full gate.** — [ ] **Step 6: Commit** —
  `feat(tools): object, hybrid, dictionary and free-form projection`

---

### Task 6: Unions, literals, special numbers, tuples, content-encoded strings

**Files:**
- Create: `Models/LiteralNode.cs`, `Models/LiteralKind.cs`, `Models/LiteralDialect.cs`,
  `Models/LiteralMarker.cs`, `Models/UnionNode.cs`, `Models/UnionKeyword.cs`,
  `Models/UnionClassification.cs`, `Models/NullableNode.cs`, `Models/SpecialNumberNode.cs`,
  `Models/TupleNode.cs`, `Models/JsonStringNode.cs`, `Models/ErrorStyle.cs`
- Create: `Projection/UnionNormalizer.cs`, `Projection/LiteralClassifier.cs`,
  `Projection/ErrorStyleClassifier.cs`, `Projection/PrefixItemsAdapter.cs` (all internal sealed)
- Modify: `Projection/SchemaProjector.cs`, `Projection/GraphKeyBuilder.cs`,
  `Models/ObjectNode.cs` construction site
- Test: `tests/.../UnionNormalizerTests.cs`, `LiteralClassifierTests.cs`,
  `ErrorStyleClassifierTests.cs`, `PrefixItemsAdapterTests.cs`

**Interfaces:**
- Consumes: Tasks 4–5 dispatch/promotion; the raw admitted-site rule (wall).
- Produces:
  - `LiteralNode { required LiteralKind Kind; required string Value; required LiteralDialect Dialect; }`
    (`Kind`: `String|Boolean`; `Dialect`: `SingleValueEnum|Const`); `const` admitted **only
    on string-typed schemas** (the DOM's `Const` is a string and cannot preserve other
    literal kinds — session 13); boolean single-value enum accepted; multi-value boolean
    enum refuses; `enum`+`const` together refuses.
  - `LiteralMarker { required string PropertyName; required LiteralKind Kind; required string Value; }` —
    computed on `ObjectNode` construction: required properties whose schema is a
    `LiteralNode`, property order (mechanical — never a name list).
  - `GraphKeyBuilder` gains
    `string UnionBranch(string parentPointer, string keyword, int index, LiteralMarker? marker)`
    once the marker model exists; marked branches use `{property}={value}` and unmarked
    branches use the ordinal index.
  - `ErrorStyle { None, EffectTag, NameData }` via `ErrorStyleClassifier`: required `_tag`
    literal → `EffectTag`; required `name` literal + required `data` → `NameData`.
  - `UnionNode { required IReadOnlyList<SchemaNode> Branches; required UnionKeyword Keyword; required UnionClassification Classification; }`
    (`Keyword`: `AnyOf|OneOf`; `Classification`: `Marked` — every object branch carries ≥1
    literal marker — else `Structural`; the 5 heterogeneous pin sites are `Structural`).
  - `UnionNormalizer` — the locked analysis order: (1) special-number check (one `number`
    branch + only NaN/Infinity string-literal branches, subset rule absorbs the combined
    literal) → `SpecialNumberNode`; (2) duplicate-`$ref` dedup by target id; (3) null-branch
    extraction → wrap result in `NullableNode { required SchemaNode Inner }`; (4) single
    branch left → that node plain; zero → error; (5) else `UnionNode` with keyword +
    classification; `anyOf`+`oneOf` together → error.
  - `PrefixItemsAdapter` — reads the admitted raw `prefixItems` array; each item parses via
    `OpenApiModelFactory.Parse<OpenApiSchema>(itemJson, OpenApiSpecVersion.OpenApi3_1, hostDocument, out var diagnostic, "json")`;
    diagnostic errors → located errors; result → `TupleNode { required IReadOnlyList<SchemaNode> Items }`;
    `items`+`prefixItems` together → error; `minItems`/`maxItems` must equal arity → else
    error. Non-admitted `prefixItems` sites stay wall errors (Task 4).
  - `type: "string"` + `ContentSchema` → `JsonStringNode { required SchemaNode Inner }`;
    `ContentMediaType` must be `application/json` → else error.

- [ ] **Step 1: Write the failing tests.** Exemplar:

```csharp
[Test]
public async Task Project_Should_Classify_Structural_Union_When_Branches_Carry_No_Markers()
{
    var host = new SchemaProjectionTestHost();
    var scenario = SpecScenario.Define(spec => spec.WithSchema("Formatter", schema =>
        schema.AnyOf(
            branch => branch.Type("boolean"),
            branch => branch.Type("object")
                .AdditionalProperties(value => value.Type("string")))));

    var result = await host.ProjectAsync(scenario);

    var union = (UnionNode)result.Schemas["Formatter"];
    await Assert.That(union.Classification).IsEqualTo(UnionClassification.Structural);
}
```

  Inventory (each in full): single-value enum literal (string + boolean); `const` on string
  → `Const` dialect; `const` on non-string → refuse; marker collection on objects (required
  literal only; optional literal and required non-literal excluded); marked `anyOf` union of
  refs; the one `oneOf`; nested union via promoted branches (marker-keyed graph keys
  asserted: `Evt#/anyOf/type=created`); unmarked-branch index fallback key
  (`Formatter#/anyOf/0`); dedup `[A,B,B]` → `[A,B]`; dedup-to-single `[A,A]` → plain
  `RefNode`; nullable extraction; nullable+dedup combined; special number (verbatim
  `Workspace.timeUsed` five-branch shape incl. combined literal, via embedded fixture
  `special-number.json` + `WithRawSchema`); special-number near-miss (extra branch) →
  ordinary union; `boolean|string-enum` parameter-style union stays union; tuple via
  admitted site (fixture reproducing `Config.plugin`); tuple arity conflict refuse;
  `items`+`prefixItems` refuse; content-encoded string (ref inner); wrong
  `contentMediaType` refuse; `EffectTag`/`NameData`/`None` classification; multi-value
  boolean enum refuse.
- [ ] **Step 2: verify fail.** — [ ] **Step 3: Implement.** — [ ] **Step 4: verify pass.**
- [ ] **Step 5: Full gate.** — [ ] **Step 6: Commit** —
  `feat(tools): union analysis, literal dialects, tuples and special numbers`

---

### Task 7: Operation surface — per-host walls + operation projection

**Files:**
- Create: `Models/SpecOperation.cs`, `Models/SpecSurface.cs`, `Models/SpecParameter.cs`,
  `Models/SpecParameterLocation.cs`, `Models/SpecRequestBody.cs`, `Models/SpecResponse.cs`,
  `Models/SpecEnvelopeShape.cs`, `Models/SpecMediaType.cs`
- Create: `Walls/DocumentWallPolicy.cs`, `Walls/PathItemWallPolicy.cs`,
  `Walls/OperationWallPolicy.cs`, `Walls/ParameterWallPolicy.cs`,
  `Walls/RequestBodyWallPolicy.cs`, `Walls/ResponseWallPolicy.cs`,
  `Walls/MediaTypeWallPolicy.cs` (internal sealed, one file each)
- Create: `Projection/OperationProjector.cs`, `Projection/EnvelopeClassifier.cs` (internal sealed)
- Create: `tests/OpenCode.Sdk.Tools.Tests/Support/OperationProjectionTestHost.cs`,
  `Support/OperationProjectionResult.cs`,
  `Fixtures/path-level-parameters.json`
- Test: `tests/.../HostWallPolicyTests.cs`, `OperationProjectorTests.cs`,
  `EnvelopeClassifierTests.cs`, `SpecMediaTypeTests.cs`

**Interfaces:**
- Consumes: Tasks 3–6 (reader, schema projection under `op:{operationId}` roots).
- Produces:
  - `SpecMediaType { required string Raw; required string Stripped; required bool IsJson; required bool IsEventStream; }`
    with `static SpecMediaType Create(string raw)` — parameter-stripped, lowercased,
    `+json` suffix detection; malformed (no `/`) → `ArgumentException` (caught into a
    located error by callers).
  - `SpecParameter { required string Name; required SpecParameterLocation Location; required SchemaNode Schema; required bool IsRequired; required bool IsDeepObject; }`
    (`Location`: `Path|Query` — `header` is not admitted);
    `SpecRequestBody { required SpecMediaType ContentType; required SchemaNode Schema; required bool IsRequired; }`;
    `SpecResponse { required int StatusCode; string? Description; SpecMediaType? ContentType; SchemaNode? Schema; required SpecEnvelopeShape EnvelopeShape; required bool IsSse; string? EffectStreamJson; }`;
    `SpecEnvelopeShape { None, Bare, Data, DataLocation, CursorData, DataHasMore }`;
    `SpecSurface { Modern, Legacy }`.
  - `SpecOperation { required string OperationId; required SpecSurface Surface; required IReadOnlyList<string> Segments; required string Method; required string Path; required bool HasWildcardPath; required bool IsWebSocket; required bool IsSse; required bool IsDeprecated; string? Summary; string? Description; required IReadOnlyList<SpecParameter> Parameters; SpecRequestBody? RequestBody; required IReadOnlyList<SpecResponse> Responses; required string RawContentHash; }`
    — `RawContentHash` empty-string until Task 8 wires the hasher (single construction
    site).
  - Wall policies render the §4.1 per-host tables: document (refuses `webhooks`, `servers`,
    `jsonSchemaDialect`, non-`schemas` components; `info`/`security`/`tags` ignored;
    bookkeeping `BaseUri`/`Self`/`Workspace`/`Metadata` exempt), path item (five methods
    only; refuses path-level `parameters`, `$ref`, `servers`), operation (admits
    `operationId` required + `summary`/`description`/`deprecated`/`parameters`/
    `requestBody`/`responses`; ignores `tags`/`security`; extensions: `x-codeSamples`
    ignored, `x-websocket` → flag, others refuse; refuses `callbacks`/`servers`), parameter
    (`path`/`query` only; `style`+`explode` only as `deepObject`+`true` pair; refuses
    `Content`, `examples`; duplicate (name,in) → error), request body (exactly one media
    entry), response (integer status keys only; refuses `headers`/`links`; 0–1 media),
    media (schema only; `x-effect-stream` on `text/event-stream` only; refuses
    `ItemSchema`/`ItemEncoding`/`PrefixEncoding`/`Encoding`/`examples`).
  - `OperationProjector` — surface split on the `v2.` operationId prefix (never the path);
    segments; wildcard flag (`/*` suffix only; mid-path `*` refuses); duplicate operationId
    → error; path-template cross-check both directions (every `{token}` declared, every
    declared path parameter present); parameters/requestBody/responses projected under
    `op:{operationId}` roots; responses status-ascending; SSE flag from media;
    `EffectStreamJson` = the `x-effect-stream` `JsonNodeExtension` serialized verbatim.
  - `EnvelopeClassifier` — JSON media only; ref-chase with identity set (cycle → error);
    exact property-name-set match `{data}`→`Data`, `{data,location}`→`DataLocation`,
    `{cursor,data}`→`CursorData`, `{data,hasMore}`→`DataHasMore`; else `Bare`; no content →
    `None`; non-JSON → `Bare`.

- [ ] **Step 1: Write the failing tests.** Exemplar:

```csharp
[Test]
public async Task Project_Should_Refuse_Path_Level_Parameters()
{
    var host = new OperationProjectionTestHost();
    var scenario = SpecScenario.Define(spec => spec.WithRawTopLevelFromFixture(
        "paths", "path-level-parameters.json"));

    var ex = await host.ProjectExpectingRefusalAsync(scenario);

    await Assert.That(ex.Message).Contains("path-level parameters");
}
```

  Inventory (each in full): modern/legacy surface split + segments; deep segments;
  wildcard flag; non-trailing wildcard refuse; websocket flag; deprecated/summary/
  description recorded; `tags`/`security` ignored clean; duplicate operationId refuse;
  unknown method key refuse; document-level `webhooks` refuse; response `headers` refuse;
  media `itemSchema` refuse (raw-injected — under 3.1 the diagnostic wall catches it;
  the test asserts the *located* refusal either way); parameter `in: header` refuse;
  parameter `style: form` refuse; content-based parameter refuse; deepObject+true
  admitted (`IsDeepObject`); bracketed parameter names verbatim; boolean-ish
  `anyOf [boolean, enum]` parameter → `UnionNode`; requestBody multi-media refuse;
  path-token cross-check both directions; envelope shapes ×4 via named-ref and inline
  fixtures + bare + none; envelope ref-cycle refuse; SSE detection + opaque
  `x-effect-stream` carried verbatim (round-trip compare); `x-effect-stream` on JSON media
  refuse; `SpecMediaType` parameter stripping / `+json` / case; responses sorted; status
  `"default"` refuse. One-off host-wall variations stay inline; fixtures carry structural
  raw payloads and no per-case scenario class/file is created.
- [ ] **Step 2: verify fail.** — [ ] **Step 3: Implement.** — [ ] **Step 4: verify pass.**
- [ ] **Step 5: Full gate.** — [ ] **Step 6: Commit** —
  `feat(tools): per-host walls and operation projection`

---

### Task 8: Orchestration seam — `SpecIngestion`, raw walk, hashes, `$ref` siblings

**Files:**
- Create: `Generator/Ingestion/Abstractions/ISpecIngestion.cs`
- Create: `Generator/Ingestion/SpecIngestion.cs` (sealed, the seam implementation)
- Create: `Generator/Ingestion/Models/SpecDocument.cs`
- Create: `Generator/Ingestion/Projection/RawContentHasher.cs`,
  `Projection/RawSiblingScanner.cs` (internal sealed)
- Modify: `tools/OpenCode.Sdk.Tools/ToolApp.cs` (register the seam + collaborators)
- Test: `tests/.../SpecIngestionTests.cs`, `RawContentHasherTests.cs`,
  `RawSiblingScannerTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `public interface ISpecIngestion { Task<SpecDocument> IngestAsync(string specPath, CancellationToken cancellationToken); }`
  - `SpecDocument { required string OpenApiVersion; required IReadOnlyList<SpecOperation> Operations; required IReadOnlyDictionary<string, SchemaNode> Schemas; required IReadOnlyDictionary<string, string> SchemaContentHashes; }`
    (both dictionaries ordinal-sorted; `OpenApiVersion` read from the raw side — the DOM
    does not retain the string).
  - `RawContentHasher` — `string Hash(JsonNode subtree)`: canonical JSON (recursively
    sorted object keys, no whitespace) → SHA-256 hex. Fills `SpecOperation.RawContentHash`
    (the raw operation object) and `SpecDocument.SchemaContentHashes` (every named schema's
    raw subtree).
  - `RawSiblingScanner` — walks raw schema positions; any object carrying `$ref` plus keys
    beyond `description`/`summary` → located error (typed members cannot distinguish a
    sibling from the proxied target — session 13).
  - `SpecIngestion(IFileSystem fs)` composition: reader → document/pathItem walls →
    schema roots (components) → operations → raw sibling scan → dangling-ref sweep over
    `SchemaNode.Children` → hashes → `ThrowIfAny` → frozen `SpecDocument`. DI: `ToolApp`
    registers `ISpecIngestion` → `SpecIngestion` singleton (collaborators constructed
    internally — they are pure and need no substitution seam).

- [ ] **Step 1: Write the failing tests.** Exemplar:

```csharp
[Test]
public async Task Hash_Should_Be_Stable_Under_Key_Reordering()
{
    var hasher = new RawContentHasher();

    var ordered = hasher.Hash(JsonNode.Parse("""{"a":1,"b":[{"x":1,"y":2}]}""")!);
    var reordered = hasher.Hash(JsonNode.Parse("""{"b":[{"y":2,"x":1}],"a":1}""")!);

    await Assert.That(ordered).IsEqualTo(reordered);
}
```

  (These two literals are sanctioned: the literal *is* the subject — canonicalization.)
  Inventory: hash differs on value change; array order preserved (reordering array items
  changes the hash); `IngestAsync` end-to-end on a builder scenario → operations +
  schemas + hashes populated, per-op hash non-empty; `$ref`+`pattern` sibling refuse;
  `$ref`+`description` sibling admitted; dangling `RefNode` sweep error; DI resolution test
  starts from `ToolApp.CreateServices(...)`, replaces `IFileSystem` with the scenario's
  instance after production registration, then resolves
  `provider.GetRequiredService<ISpecIngestion>()`. Full-ingestion repeat determinism is
  asserted once, against the pinned document in Task 10; this task keeps the distinct
  canonical-hash unit properties.
- [ ] **Step 2: verify fail.** — [ ] **Step 3: Implement.** — [ ] **Step 4: verify pass.**
- [ ] **Step 5: Full gate.** — [ ] **Step 6: Commit** —
  `feat(tools): ingestion seam with raw-content hashes and sibling scan`

---

### Task 9: Library-upgrade tripwires + DOM-boundary guards

**Files:**
- Test: `tests/.../Tripwires/PrefixItemsTripwireTests.cs`,
  `Tripwires/DomMemberInventoryTests.cs`, `Tripwires/SpecIrSnapshotTests.cs`,
  `Guards/IngestionBoundaryTests.cs`

**Interfaces:**
- Consumes: `ISpecIngestion` (Task 8), the pinned spec fixture (Task 2 link), Verify.TUnit.
- Produces: the four §4.1 tripwire/guard suites later slices rely on.

- [ ] **Step 1: Write the tripwires (they pass immediately — their red day is a library
  bump; each carries a comment naming what a failure means):**
  - `PrefixItemsTripwireTests` — load the pinned spec; assert
    `Config.plugin/items/anyOf[1]` still exposes `prefixItems` via `UnrecognizedKeywords`
    (a newer library typing it must fail this loudly).
  - `DomMemberInventoryTests` — reflection over every consumed DOM type
    (`OpenApiDocument`, `OpenApiPathItem`, `OpenApiOperation`, `OpenApiParameter`,
    `OpenApiRequestBody`, `OpenApiResponse`, `OpenApiMediaType`, `OpenApiSchema`): sorted
    public-member names plus fresh-instance default values of `bool?`/`bool` members —
    compared against a committed baseline string (in a `DomInventoryData` static class —
    testing-style §3 home 3). A new/removed member or flipped default fails with the
    member named.
  - `SpecIrSnapshotTests` — `IngestAsync(spec/openapi.json)` → deterministic multi-line
    rendering (operations with surface/method/path/flags/envelope shapes/hash prefixes;
    ordinal-sorted schema keys with node kinds) → `Verify()` snapshot. Changes only on a
    spec refresh or admit-rule change — both reviewed events.
- [ ] **Step 2: Write the guards:**
  - `IngestionBoundaryTests` —
    (a) reflection over `SpecDocument`'s transitive public surface: no type from the
    `Microsoft.OpenApi` assembly reachable;
    (b) source scan: enumerate `tools/OpenCode.Sdk.Tools/**/*.cs` through the real
    `RealFileSystem`; any file outside `Generator/Ingestion/` containing
    `using Microsoft.OpenApi` fails with the path named.
- [ ] **Step 3: Run — all green against the pin;** verify the snapshot's `.verified.txt`
  is committed and stable across two runs.
- [ ] **Step 4: Full gate.** — [ ] **Step 5: Commit** —
  `test(tools): library tripwires and ingestion boundary guards`

---

### Task 10: Full pinned-spec landmark smoke

**Files:**
- Test: `tests/OpenCode.Sdk.Tools.Tests/PinnedSpecSmokeTests.cs`

**Interfaces:**
- Consumes: `ISpecIngestion` + `RealFileSystem` + the linked pinned spec.
- Produces: the structural gate every future spec refresh runs through. **No count
  assertions.**

- [ ] **Step 1: Write the tests** — the landmark inventory, asserted against SpecIR:
  full-document ingest succeeds; both surfaces present; `v2.session.events` `IsSse` +
  `EffectStreamJson` non-null; `v2.fs.read` wildcard; `v2.pty.connect` websocket flag;
  dotted key `session.status` present; `SessionDurableEvent` = `UnionNode`
  (`OneOf`, `Marked`); `Workspace.timeUsed` = `SpecialNumberNode`;
  `Config.formatter` = `UnionNode` `Structural`; `MoveSessionError` `ErrorStyle.NameData`;
  `effect_HttpApiError_BadRequest` `ErrorStyle.EffectTag`; `v2.session.list` 200
  `CursorData` and 400 branches dedup to `[InvalidCursorError, InvalidRequestError]`;
  `v2.session.history` 200 `DataHasMore`; `v2.agent.list` 200 `DataLocation`; a
  `None`-envelope response exists; `v2.session.active` data resolves to `DictionaryNode`;
  the six unrestricted landmark sites (`AssistantMessage.structured`,
  `AgentConfig` AP, `ToolListItem.parameters`, `Workspace.extra/anyOf/0` via its graph
  key, `SessionMessageToolStateCompleted.result`, the `tui.control.response` request
  root) are `UnrestrictedNode`; `Config.plugin` tuple = `TupleNode` arity 2; repeated
  ingest → identical schema-key sequence and hash set. (If a landmark contradicts the
  pinned wire, that is evidence — stop and classify; never patch the assertion.)
- [ ] **Step 2: Run — green against the real spec** (the parser-era failure mode is the
  RED reference: session 12 records why these landmarks exist).
- [ ] **Step 3: Full gate.** — [ ] **Step 4: Commit** —
  `test(tools): full pinned-spec landmark smoke`

---

### Task 11: Docs pass, push, PR

**Files:**
- Modify: `docs/ROADMAP.md`; `docs/research/00-research-log.md` (only if execution produced
  findings beyond sessions 12–14)

- [ ] **Step 1:** ROADMAP status: Slice 1 landed (ingestion + SpecIR under
  `Generator/Ingestion/`, full ToolApp hosting + centralized scenario/builder test
  infrastructure live; `generate` remains a fail-loud stub until slice 3); next is Slice 2
  planning (issue #3).
- [ ] **Step 2:** Research-log entry only for new evidence (wall refusals the plan did not
  predict, analyzer arbitrations, library surprises).
- [ ] **Step 3:** Final full gate on the branch.
- [ ] **Step 4:** Push `feature/slice-01-ingestion-specir`; PR
  `feat(tools): slice 1 — ingestion + SpecIR` (body: what landed; the honest "generation
  remains stubbed while full hosting/global options are live" note; deviations with levels;
  `Closes #2`). Three CI legs green; maintainer merge.

---

## Handoff to Slice 2 (Binder + curation v0)

- **`ToolApp.CreateServices` is the only composition path.** Slice 2 extends it with Binder
  and curation collaborators; tests replace seams after the production registrations rather
  than constructing a second root. Global settings, MEL providers, and the interceptor are
  already live.
- **`SpecDocument` is the Binder's sole spec-side input**; the reachable closure walks
  `SchemaNode.Children`/`RefNode.Target` keys. Envelope-classified response-root schemas
  are already classified per response — the Binder subtracts them from model emission
  (generator spec §4.2).
- **Fingerprint source:** `SpecOperation.RawContentHash` + `SpecDocument.SchemaContentHashes`
  — the Binder composes §9's two kinds; it never re-reads the spec.
- **Recorded facts consumed downstream:** `SpecEnvelopeShape`, `ErrorStyle`,
  `LiteralMarker`s, `UnionClassification` (structural unions have no tag-dispatch
  converter — emission shape is a §15 open item), `IsSse`+`EffectStreamJson`,
  `IsWebSocket`, `SpecMediaType.Stripped`, `Description`/`Format`, `IsDeprecated`.
- **Not produced here:** curation, coverage checks, closure computation, fingerprint
  persistence, EmitPlan, emitters, Writer, `generate` wiring (still the fail-loud stub).
