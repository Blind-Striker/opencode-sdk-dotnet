# Slice 1 — Parser + SpecIR Implementation Plan

Date: 2026-08-10

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development
> (recommended) or deniz-process:executing-plans to implement this plan task-by-task. Steps
> use checkbox (`- [ ]`) syntax for tracking.

**Goal:** build the wire-faithful SpecIR and the fail-closed OpenAPI 3.1 parser behind it
(generator spec §4.1) — operations, schema graph with every node kind the pinned spec
needs, literal markers in both dialects, the parse-time normalizations, and the dialect
wall — plus one hand-written quirk fixture per construct and a full pinned-spec smoke test.

**Architecture:** `SpecParser` (in `tools/OpenCode.Sdk.Tools/Generator/Parsing/`) reads
`spec/openapi.json` through TestableIO's `IFileSystem`, walks the document with
`System.Text.Json`'s `JsonDocument`/`JsonElement` (in-box on net10.0 — no new STJ pin),
and produces an immutable `SpecDocument`: a flat schema graph (named schemas + promoted
inline types under deterministic keys) and a `SpecOperation` list. Everything unknown is
refused with batched, located error messages (`SpecParseException`). Zero C# concepts in
the IR — naming is the Binder's job (slice 2). The CLI does not change: `generate` stays
the fail-loud stub, no `ToolApp`/DI edits, no workflow edits.

**Tech Stack:** TestableIO trio 22.2.0 (newest stable, verified against NuGet
2026-08-10: `TestableIO.System.IO.Abstractions` for the library,
`.TestingHelpers` + `.Wrappers` for tests) plus
`TestableIO.System.IO.Abstractions.Analyzers` 2022.0.0 (rules IO0001–IO0011: direct
`File`/`Directory`/`Path`/`FileInfo`/`FileStream`/`DirectoryInfo`/`StreamReader` usage
must go through `IFileSystem` — mechanically enforces the TestableIO seam, wired
repo-wide like every other analyzer), System.Text.Json (in-box), TUnit on
Microsoft.Testing.Platform (already pinned).

## Global Constraints

- `LangVersion=14.0`, `AnalysisLevel=10.0` — deliberate numeric pins; never "fix" to
  `latest` (AGENTS.md Hard Rules).
- Full analyzer wall + `TreatWarningsAsErrors=true` applies to `tools/`:
  guards on public inputs (CA1062: `ArgumentNullException.ThrowIfNull` /
  `ArgumentException.ThrowIfNullOrWhiteSpace`), MA0048 one type per file, IDE0130
  folder = namespace, MA0051 method length (decompose the dispatch into small privates),
  culture-invariant formatting (CA1305/MA0011: any interpolation with non-string values
  goes through `FormattableString.Invariant` or an explicit `CultureInfo.InvariantCulture`),
  ordinal string comparisons (`StringComparison.Ordinal` everywhere). The parser is fully
  synchronous — if any `await` appears, the `ConfigureAwait(false)` triple applies.
- Analyzer misfires are arbitrated per-rule with a winner-naming comment
  (`.editorconfig` pattern) — never rolled back wholesale, never suppressed inline without
  arbitration.
- Test naming: `{Symbol}_Should_{Expected_Behavior}[_When_{Condition}]`. Test classes:
  the SUT is `SpecParser` for nearly every test in this slice; to keep single-file classes
  navigable the plan uses one class per parsing area named `SpecParser{Area}Tests`
  (e.g. `SpecParserUnionTests`), flat in the test project root, one class per file —
  an agreed slice-1 interpretation of the `{Sut}Tests` convention. `SpecMediaTypeTests`
  has its own genuine SUT. Test code is exempt from the ConfigureAwait triple.
- TUnit/MTP syntax: `[Test]`, `await Assert.That(x).IsEqualTo(y)`; exception capture is
  `var ex = await Assert.That(() => ...).Throws<SpecParseException>();` then assert on
  `ex!.Message` with `.Contains(...)`. If the pinned TUnit's assertion surface differs in
  detail, adapt in place — level-0 deviation.
- Central package management: the only new pins are the TestableIO trio at **22.2.0**
  and `TestableIO.System.IO.Abstractions.Analyzers` at **2022.0.0** (newest stable as of
  2026-08-10). Before pinning at execution time, re-check with
  `dotnet package search <id> --exact-match` and pin the newer stable if one exists.
- The IO analyzer rides the wall at its default severities (all rules enabled, Warning —
  TWAE escalates to error); **no `.editorconfig` section for it** unless a rule misfires,
  and then the fix is the standard per-rule arbitration comment, never a rollback.
  Consequence inside this slice: even test code reaches `Path` through
  `IFileSystem.Path` (IO0006).
- No count assertions against the pinned full spec — shape and existence assertions on
  named constructs only (generator spec §11: counts are research-doc facts).
- Determinism: schema-graph keys ordinal-sorted; operation list in document order;
  responses sorted ascending by status; no wall-clock, no randomness.
- Defensive programming is the default: unknown constructs refuse with a located error;
  internal invariants assert; silent fallbacks are forbidden (the recorded tolerances in
  this plan are the known-ignored keyword list and the opaque `x-effect-stream` carry).
- Everything temporary goes to `.scratchpad/` (gitignored).
- After every task (before its commit): run the local Slopwatch gate
  `dotnet tool run slopwatch analyze --exclude ".scratchpad/**,external/**" --fail-on warning`.
- **The full gate** (referenced by every task) = `dotnet build --configuration Release`
  → `dotnet test --configuration Release --no-build` →
  `dotnet format --verify-no-changes --no-restore` → the Slopwatch command above. All
  four must be clean.
- All artifacts in English; Conventional Commits; per-task commits on the slice branch
  are the agreed development loop (no per-commit approval; master merges via PR only).
- Contradictions with a sealed spec: stop and classify per
  `docs/agents/deviation-protocol.md`. Subagents never self-resolve level 2+.
- Work happens on branch `feature/slice-01-parser-specir` in a worktree
  (deniz-process:using-git-worktrees).
- Out of scope (hidden-scope ban): Binder, curation, emitters, Writer, generated output,
  any `generate` pipeline wiring, fingerprint computation, `ToolApp`/DI changes,
  CI workflow changes.

## Planning-time spec findings (approved with this plan)

Probing the pinned spec during planning surfaced three constructs the sealed generator
spec §4.1 inventory does not name. A parser built strictly to the sealed inventory would
refuse the pinned spec, so §4.1 is corrected in the planning session's docs commit
(deviation protocol: the doc carrying the gap is corrected in the same change; maintainer
approval of this plan covers these corrections):

1. **`oneOf` union.** `SessionDurableEvent` is a `oneOf` of 28 refs — the one `oneOf` in
   the document. §4.1's union node reads "`anyOf` + literal-marker analysis"; corrected to
   "`anyOf`/`oneOf` (keyword recorded on the node)".
2. **Tuple.** `Config.plugin` items contain
   `{"type":"array","prefixItems":[{"type":"string"},{"type":"object"}],"minItems":2,"maxItems":2}` —
   a fixed-arity tuple. Added to the node-kind inventory.
3. **Content-encoded string.** `SessionDurableEventStream` and `V2EventStream` are
   `{"type":"string","contentSchema":{...},"contentMediaType":"application/json"}` — a
   JSON-in-string wrapper around the durable event unions (the former is referenced from
   `v2.session.events`'s SSE media schema; the latter is currently unreferenced). Added to
   the inventory.
4. **`patternProperties` dictionary.** `v2.session.active`'s 200 payload is
   `{"type":"object","patternProperties":{"^ses":{"$ref":".../SessionActive"}}}` — a
   pattern-keyed dictionary (the document's only occurrence). Parsed as a dictionary
   node; the key pattern is validation-only and dropped like every other validation
   keyword (recorded tolerance). Multiple patterns, or combination with
   `properties`/`additionalProperties`, refuse.

Two extension dispositions are also pinned (the wall refuses any *other* `x-*` key):
`x-codeSamples` (on all operations — docs metadata, known-ignored) and `x-websocket`
(marks `v2.pty.connect` — recorded as a boolean flag; it is exactly the class of
semantic extension the wall exists to catch). `x-effect-stream` stays opaque per §4.1.

Verified-safe facts the plan relies on (probed 2026-08-10 against the pinned spec):
response and request media objects carry at most one content type; parameters appear only
in `path`/`query`; enums hold only string and boolean values; `additionalProperties` is
only `false` or a schema; every `required` name has a matching property; six objects carry
*both* `properties` and an `additionalProperties` schema (hybrid); 161 bare
`{"type":"object"}` free-form nodes exist; the special-value-number `anyOf` has five
branches including one *multi-literal* branch `["Infinity","-Infinity","NaN"]`.

## SpecIR at a glance (reference — each task restates what it needs)

All types live in `tools/OpenCode.Sdk.Tools/Generator/Parsing/`, namespace
`OpenCode.Sdk.Tools.Generator.Parsing`, one file per type (MA0048), all public records
immutable (`required`/`init`, `IReadOnlyList`/`IReadOnlyDictionary`).

| Type | Role |
|---|---|
| `SpecParser` | entry point: `SpecDocument Parse(string specPath)` over `IFileSystem` |
| `SpecParseException` | batched refusal; `IReadOnlyList<string> Errors` |
| `SpecDocument` | `OpenApiVersion`, `Operations`, `Schemas` (ordinal-sorted keys) |
| `SpecOperation`, `SpecParameter`, `SpecRequestBody`, `SpecResponse`, `SpecMediaType` | operation surface |
| `SpecSurface`, `SpecParameterLocation`, `SpecEnvelopeShape` | operation-side enums |
| `SchemaNode` (abstract; `Description?`, `Children`) | graph node base |
| `PrimitiveNode`, `EnumNode`, `LiteralNode`, `ObjectNode` (+`SpecProperty`), `DictionaryNode`, `FreeFormObjectNode`, `ArrayNode`, `TupleNode`, `UnionNode`, `NullableNode`, `RefNode`, `SpecialNumberNode`, `JsonStringNode` | node kinds |
| `LiteralMarker` | marker fact on `ObjectNode` (property name + literal value) |
| `PrimitiveKind`, `LiteralKind`, `LiteralDialect`, `AdditionalPropertiesKind`, `UnionKeyword`, `ErrorStyle` | node-side enums |

**Schema-graph keys (locked format — the Binder consumes these):** named schemas use the
wire name verbatim (`Session`, `session.status`). Promoted inline types use
`{root}#{pointer}` where root is the owning named schema's wire name or
`op:{operationId}` for operation-rooted schemas, and pointer is built from segments
`/properties/{name}`, `/items`, `/additionalProperties`, `/patternProperties`,
`/prefixItems/{index}`, `/contentSchema`, `/anyOf/{branch}`, `/oneOf/{branch}`,
`/parameters/{name}`, `/requestBody`, `/responses/{status}`. A union-branch segment `{branch}` is
`{prop}={value}` using the branch object's alphabetically-first literal marker
(e.g. `/anyOf/type=text`); branches without a marker use the ordinal index. Never a
document-global counter (research doc 08's NSwag renumbering disqualifier). Promotion
applies to inline `ObjectNode`/`UnionNode`/`EnumNode` at non-root positions: the node
parser registers the node under its key and returns a `RefNode` pointing at it.
Promotion does not reset the root — a branch inside a promoted union still keys from the
owning named schema (e.g. `GlobalEvent#/properties/payload/anyOf/type=created`). A key
collision is a parse error (defensive invariant).

**Dialect wall (locked known-set; everything else refuses with a located message):**

| Level | Known keys / values | Disposition |
|---|---|---|
| document | `openapi` (must start `3.1.`), `info`, `paths`, `components` (only member: `schemas`), `security`, `tags` | version recorded; `info`/`security`/`tags` ignored |
| path item | `get` `put` `post` `delete` `patch` | each parsed as an operation |
| operation | `operationId` (required), `summary`, `description`, `tags`, `security`, `deprecated`, `parameters`, `requestBody`, `responses`, `x-codeSamples`, `x-websocket` | `tags`/`security`/`x-codeSamples` ignored; rest recorded/parsed |
| parameter | `name`, `in` (`path`/`query`/`header`), `schema`, `required` (default false), `style` + `explode` (only `deepObject`+`true`, together) | recorded |
| requestBody | `content` (exactly one media entry), `required` (default false) | recorded |
| response | `description`, `content` (0 or 1 media entries) | recorded |
| media object | `schema`; on `text/event-stream` media additionally `x-effect-stream` | schema parsed; extension carried opaque |
| schema node | `type` (single string: `string` `number` `integer` `boolean` `object` `array` `null`), `$ref` (only `#/components/schemas/{name}`), `properties`, `required`, `additionalProperties` (`false` or schema), `patternProperties` (single pattern, alone — a dictionary spelling), `items`, `prefixItems`, `enum`, `const`, `anyOf`, `oneOf`, `description`, `format`, `contentSchema` + `contentMediaType` (only `application/json`) | parsed/recorded |
| schema node (known-ignored) | `pattern`, `minimum`, `exclusiveMinimum`, `maximum`, `minItems`, `maxItems` (the last two must match the arity when `prefixItems` is present) | validation-only keywords, deliberately dropped (recorded tolerance); the `patternProperties` key pattern joins them |

Explicitly refused (wall tests exist): `allOf`, `not`, `discriminator`, type arrays,
`additionalProperties: true`, `nullable` (3.0-ism), `default`, external/other `$ref`
targets, unknown `x-*` keys, unknown styles/locations/methods, multiple content types,
multi-value boolean enums, `items`+`prefixItems` together, `minItems`/`maxItems`
conflicting with `prefixItems` arity, `patternProperties` with multiple patterns or
combined with `properties`/`additionalProperties`, `x-effect-stream` on non-SSE media.

**Parse order (locked):** components.schemas first (graph assembly + promotion), then
paths (operations; envelope classification resolves refs against the completed graph),
then the dangling-ref sweep over every node (via `SchemaNode.Children`), then
`ThrowIfAny` on the error collector. Error messages are `{location}: {problem}` with
locations like `schema 'Config' at /properties/plugin` or
`operation 'v2.session.list' response 400`.

---

### Task 1: Package pins, `SpecParseException`, document-level parser shell

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `Directory.Build.props` (IO analyzer joins the repo-wide analyzer wall)
- Modify: `tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj`
- Modify: `tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj`
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecParseException.cs`
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecParser.cs`
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecParseErrorCollector.cs` (internal)
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecDocument.cs`
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecOperation.cs` (minimal shell — grows in tasks 7–9)
- Create: `tests/OpenCode.Sdk.Tools.Tests/SpecFixture.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecParserTests.cs`

**Interfaces:**
- Consumes: `IFileSystem` (`System.IO.Abstractions`, TestableIO 22.2.0); `MockFileSystem`
  (`System.IO.Abstractions.TestingHelpers`).
- Produces (all later tasks build on these exact shapes):
  - `public sealed class SpecParser { public SpecParser(IFileSystem fileSystem); public SpecDocument Parse(string specPath); }`
  - `public sealed class SpecParseException : Exception { public IReadOnlyList<string> Errors { get; } }`
    (plus the three standard constructors for CA1032, with empty `Errors`)
  - `public sealed record SpecDocument { required string OpenApiVersion; required IReadOnlyList<SpecOperation> Operations; required IReadOnlyDictionary<string, SchemaNode> Schemas; }`
    — in this task `Operations` and `Schemas` are always empty (their content arrives in
    tasks 2 and 7); `SpecOperation` exists as an empty record shell so `SpecDocument`
    compiles (task 7 fills it).
  - `public abstract record SchemaNode { public string? Description { get; init; } public abstract IEnumerable<SchemaNode> Children { get; } }`
    in `SchemaNode.cs`, created now so `SpecDocument.Schemas` is properly typed from the
    start (task 2 adds the concrete kinds).
  - internal `SpecParseErrorCollector` — `void Add(string location, string problem)`,
    `bool HasErrors`, `void ThrowIfAny()`.
  - Test helper `internal static class SpecFixture` —
    `SpecDocument Parse(string documentJson)`,
    `SpecDocument ParseSchemas(string schemasJson)`,
    `SpecDocument ParsePaths(string pathsJson, string schemasJson = "{}")` — builds a
    `MockFileSystem`, writes the JSON to `spec/openapi.json`, parses.

- [ ] **Step 1: Add the package pins**

In `Directory.Packages.props`, under `<!-- third-party analyzers -->` add (alphabetical):

```xml
    <PackageVersion Include="TestableIO.System.IO.Abstractions.Analyzers" Version="2022.0.0"/>
```

Under `<!-- third-party packages -->` add (alphabetical):

```xml
    <PackageVersion Include="TestableIO.System.IO.Abstractions" Version="22.2.0"/>
```

Under `<!-- test packages -->` add:

```xml
    <PackageVersion Include="TestableIO.System.IO.Abstractions.TestingHelpers" Version="22.2.0"/>
    <PackageVersion Include="TestableIO.System.IO.Abstractions.Wrappers" Version="22.2.0"/>
```

(Re-check newest stable first — Global Constraints. `Wrappers` is referenced only by the
test project, first used in task 10; pinning all four rows now keeps the CPM change in
one commit.)

In `Directory.Build.props`, append to the repo-wide analyzer ItemGroup (after the
`Meziantou.Analyzer` entry, same shape as its siblings):

```xml
    <PackageReference Include="TestableIO.System.IO.Abstractions.Analyzers">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
```

(2022.0.0 is the analyzer's newest stable — an older-Roslyn build. If the current
compiler refuses to load it, that is a finding: stop and bring it to the maintainer —
level 2, the plan's dependency choice is corrected, not worked around.)

In `tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj` add to the existing ItemGroup:

```xml
    <PackageReference Include="TestableIO.System.IO.Abstractions"/>
```

In `tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj` add:

```xml
    <PackageReference Include="TestableIO.System.IO.Abstractions.TestingHelpers"/>
```

- [ ] **Step 2: Write the failing tests**

`tests/OpenCode.Sdk.Tools.Tests/SpecFixture.cs`:

```csharp
using System.IO.Abstractions.TestingHelpers;
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

internal static class SpecFixture
{
    public const string SpecPath = "spec/openapi.json";

    public static SpecDocument Parse(string documentJson)
    {
        MockFileSystem fileSystem = new();
        fileSystem.AddFile(SpecPath, new MockFileData(documentJson));
        return new SpecParser(fileSystem).Parse(SpecPath);
    }

    public static SpecDocument ParseSchemas(string schemasJson) =>
        Parse($$"""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {{schemasJson}} }
        }
        """);

    public static SpecDocument ParsePaths(string pathsJson, string schemasJson = "{}") =>
        Parse($$"""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {{pathsJson}},
          "components": { "schemas": {{schemasJson}} }
        }
        """);
}
```

`tests/OpenCode.Sdk.Tools.Tests/SpecParserTests.cs`:

```csharp
using System.IO.Abstractions.TestingHelpers;
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserTests
{
    [Test]
    public async Task Parse_Should_Return_Empty_Document_When_Spec_Has_No_Operations_Or_Schemas()
    {
        var document = SpecFixture.ParseSchemas("{}");

        await Assert.That(document.OpenApiVersion).IsEqualTo("3.1.0");
        await Assert.That(document.Operations).IsEmpty();
        await Assert.That(document.Schemas).IsEmpty();
    }

    [Test]
    public async Task Parse_Should_Ignore_Security_And_Tags_Sections()
    {
        var document = SpecFixture.Parse("""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {} },
          "security": [],
          "tags": [ { "name": "global", "description": "Global server routes." } ]
        }
        """);

        await Assert.That(document.OpenApiVersion).IsEqualTo("3.1.0");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_OpenApi_Version_Is_Not_3_1()
    {
        var ex = await Assert.That(() => SpecFixture.Parse("""
        {
          "openapi": "3.0.3",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {} }
        }
        """)).Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("3.0.3");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Top_Level_Key_Is_Unknown()
    {
        var ex = await Assert.That(() => SpecFixture.Parse("""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {} },
          "webhooks": {}
        }
        """)).Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("webhooks");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Components_Member_Is_Not_Schemas()
    {
        var ex = await Assert.That(() => SpecFixture.Parse("""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {}, "responses": {} }
        }
        """)).Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("responses");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Spec_File_Is_Missing()
    {
        var parser = new SpecParser(new MockFileSystem());

        var ex = await Assert.That(() => parser.Parse("spec/openapi.json"))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("spec/openapi.json");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Json_Is_Malformed()
    {
        var ex = await Assert.That(() => SpecFixture.Parse("{ not json"))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("JSON");
    }

    [Test]
    public async Task SpecParser_Should_Guard_Null_FileSystem()
    {
        await Assert.That(() => new SpecParser(null!)).Throws<ArgumentNullException>();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests`
Expected: build FAILS with CS0246 (`SpecParser`/`SpecDocument`/`SpecParseException` not
found) — the red state for scaffolding.

- [ ] **Step 4: Implement**

`tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecParseException.cs`:

```csharp
namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>Batched parser refusal: every collected dialect or structure error in one throw.</summary>
public sealed class SpecParseException : Exception
{
    /// <summary>Creates the exception from the batched parse errors.</summary>
    public SpecParseException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>Standard constructor; carries no batched errors.</summary>
    public SpecParseException()
    {
        Errors = [];
    }

    /// <summary>Standard constructor; carries no batched errors.</summary>
    public SpecParseException(string message)
        : base(message)
    {
        Errors = [];
    }

    /// <summary>Standard constructor; carries no batched errors.</summary>
    public SpecParseException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [];
    }

    /// <summary>The batched parse errors, in document order.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? "Spec parse failed."
            : FormattableString.Invariant(
                $"Spec parse failed with {errors.Count} error(s):\n{string.Join('\n', errors)}");
    }
}
```

`tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecParseErrorCollector.cs`:

```csharp
namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>Accumulates located parse errors so refusals surface batched, not one at a time.</summary>
internal sealed class SpecParseErrorCollector
{
    private readonly List<string> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public void Add(string location, string problem) =>
        _errors.Add(FormattableString.Invariant($"{location}: {problem}"));

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
        {
            throw new SpecParseException([.. _errors]);
        }
    }
}
```

`tools/OpenCode.Sdk.Tools/Generator/Parsing/SchemaNode.cs`:

```csharp
namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>Base of every wire-faithful schema-graph node (generator spec §4.1).</summary>
public abstract record SchemaNode
{
    /// <summary>The spec's description text, when present (Binder XML-doc input).</summary>
    public string? Description { get; init; }

    /// <summary>Direct child nodes; drives graph-wide sweeps (dangling-ref validation).</summary>
    public abstract IEnumerable<SchemaNode> Children { get; }
}
```

`tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecOperation.cs` (shell; tasks 7–9 add
members):

```csharp
namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>A single wire operation (generator spec §4.1); populated by the paths parser.</summary>
public sealed record SpecOperation;
```

`tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecDocument.cs`:

```csharp
namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>The wire-faithful SpecIR root: operations plus the flat schema graph.</summary>
public sealed record SpecDocument
{
    /// <summary>The document's <c>openapi</c> version string (always 3.1.x).</summary>
    public required string OpenApiVersion { get; init; }

    /// <summary>All operations, in document order.</summary>
    public required IReadOnlyList<SpecOperation> Operations { get; init; }

    /// <summary>Named schemas plus promoted inline types, keys ordinal-sorted.</summary>
    public required IReadOnlyDictionary<string, SchemaNode> Schemas { get; init; }
}
```

`tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecParser.cs` — document-level walk:

```csharp
using System.IO.Abstractions;
using System.Text.Json;

namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>Parses the pinned OpenAPI 3.1 document into the wire-faithful SpecIR.</summary>
public sealed class SpecParser
{
    private readonly IFileSystem _fileSystem;

    /// <summary>Creates the parser over the injected filesystem (TestableIO seam).</summary>
    public SpecParser(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>Parses the spec file; refuses unknown constructs with batched errors.</summary>
    public SpecDocument Parse(string specPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specPath);
        if (!_fileSystem.File.Exists(specPath))
        {
            throw new SpecParseException([FormattableString.Invariant(
                $"document: spec file '{specPath}' does not exist")]);
        }

        var text = _fileSystem.File.ReadAllText(specPath);
        using var json = ParseJson(text);
        return ParseDocument(json.RootElement);
    }

    private static JsonDocument ParseJson(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new SpecParseException(
                FormattableString.Invariant($"document: not valid JSON — {exception.Message}"),
                exception);
        }
    }

    private static SpecDocument ParseDocument(JsonElement root)
    {
        SpecParseErrorCollector errors = new();
        var version = ReadVersion(root, errors);
        // Document-key wall: openapi, info, paths, components, security, tags.
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("openapi" or "info" or "paths" or "components"
                or "security" or "tags"))
            {
                errors.Add("document", FormattableString.Invariant(
                    $"unknown top-level key '{property.Name}'"));
            }
        }

        var schemas = ReadSchemas(root, errors);
        errors.ThrowIfAny();
        return new SpecDocument
        {
            OpenApiVersion = version,
            Operations = [],
            Schemas = schemas,
        };
    }

    private static string ReadVersion(JsonElement root, SpecParseErrorCollector errors)
    {
        if (!root.TryGetProperty("openapi", out var version)
            || version.ValueKind is not JsonValueKind.String)
        {
            errors.Add("document", "missing 'openapi' version string");
            return string.Empty;
        }

        var text = version.GetString() ?? string.Empty;
        if (!text.StartsWith("3.1.", StringComparison.Ordinal))
        {
            errors.Add("document", FormattableString.Invariant(
                $"unsupported OpenAPI version '{text}' — the dialect wall accepts 3.1.x only"));
        }

        return text;
    }

    private static IReadOnlyDictionary<string, SchemaNode> ReadSchemas(
        JsonElement root, SpecParseErrorCollector errors)
    {
        SortedDictionary<string, SchemaNode> graph = new(StringComparer.Ordinal);
        if (!root.TryGetProperty("components", out var components))
        {
            return graph;
        }

        foreach (var member in components.EnumerateObject())
        {
            if (!string.Equals(member.Name, "schemas", StringComparison.Ordinal))
            {
                errors.Add("document", FormattableString.Invariant(
                    $"unknown components member '{member.Name}'"));
            }
        }

        return graph;
    }
}
```

(If an analyzer objects to a detail — e.g. prefers a `HashSet` over the `or` pattern —
adapt in place; level 0. The `or` pattern keeps the known-set greppable.)

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests`
Expected: PASS (all 8 tests).

- [ ] **Step 6: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props Directory.Build.props tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): spec parser shell with OpenAPI 3.1 document gate"
```

---

### Task 2: Schema node core — primitives, enum, array, ref, keyword wall, promotion

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/PrimitiveNode.cs`, `PrimitiveKind.cs`,
  `EnumNode.cs`, `ArrayNode.cs`, `RefNode.cs`
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SchemaNodeParser.cs` (internal)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecParser.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecParserSchemaNodeTests.cs`

**Interfaces:**
- Consumes: task 1's `SchemaNode`, `SpecParseErrorCollector`, `SpecFixture.ParseSchemas`,
  and `SpecParser`'s `ReadSchemas` seam.
- Produces:
  - `public sealed record PrimitiveNode : SchemaNode { required PrimitiveKind Kind; string? Format; }`
    with `public enum PrimitiveKind { String, Number, Integer, Boolean }`
  - `public sealed record EnumNode : SchemaNode { required IReadOnlyList<string> Values; }`
  - `public sealed record ArrayNode : SchemaNode { required SchemaNode Item; }`
  - `public sealed record RefNode : SchemaNode { required string Target; }` — `Target` is
    a schema-graph key (wire name or promoted key).
  - internal `SchemaNodeParser` — created per parse with the error collector and the
    graph dictionary; `SchemaNode? Parse(JsonElement schema, string root, string pointer)`
    where `root` is the graph root key (`Session` or `op:v2.session.list`) and `pointer`
    the position within it (`""` for the root itself). Non-null `pointer` + eligible node
    kind (this task: `EnumNode`; task 3 adds `ObjectNode`; task 4 adds `UnionNode`) ⇒
    register under `{root}#{pointer}` and return a `RefNode` to that key. Key collision ⇒
    error. Later tasks extend the same dispatch — **the known-construct set grows task by
    task; do not add refusal tests here for keywords a later task legitimizes**
    (`properties`, `anyOf`, `oneOf`, `const`, `prefixItems`, `contentSchema` are refused
    by the *implementation* until their task lands, but only the final wall gets tests).
  - `SpecParser` change: `ReadSchemas` parses every named schema through
    `SchemaNodeParser` (root = wire name, pointer = ""), then runs the **dangling-ref
    sweep**: walk every graph node's transitive `Children`; a `RefNode` whose `Target` is
    not a graph key ⇒ error `schema '{root}': unresolved ref '{target}'`.

Parsing rules (exact dispatch on the schema object's keys, after the per-key wall check):
1. `$ref` present ⇒ must match `#/components/schemas/{name}` (prefix strip; anything else
   refuses); no sibling keys except none (pinned spec has bare refs) ⇒ `RefNode`.
2. `type: "string"|"number"|"integer"|"boolean"` without `enum`/`const`/`contentSchema` ⇒
   `PrimitiveNode` (record `format` string when present).
3. `type: "string"` + `enum` (≥2 string values) ⇒ `EnumNode` (single-value and boolean
   enums are task 4; until then the implementation refuses them with
   `single-value enum not yet handled` — no test pins that message).
4. `type: "array"` + `items` ⇒ `ArrayNode` (recurse at `{pointer}/items`); no `items` ⇒
   refuse (`array without items`).
5. `type` is a JSON array ⇒ refuse (`type arrays are outside the dialect`).
6. Known-ignored keywords `pattern`/`minimum`/`exclusiveMinimum`/`maximum`/`minItems`/
   `maxItems` are dropped (task 5 adds the one exception: with `prefixItems`,
   `minItems`/`maxItems` must match the arity); `description` is recorded onto the
   produced node.
7. Any other key (including `allOf`, `not`, `discriminator`, `nullable`, `default`,
   unknown `x-*`) ⇒ refuse with the keyword named. Refusal adds the error and returns
   `null`; parents propagate `null` without cascading extra errors.

- [ ] **Step 1: Write the failing tests**

`tests/OpenCode.Sdk.Tools.Tests/SpecParserSchemaNodeTests.cs` — full list (each body
follows the two patterns shown; fixture JSON inline via `SpecFixture.ParseSchemas`):

```csharp
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserSchemaNodeTests
{
    [Test]
    public async Task Parse_Should_Produce_Primitive_Nodes_For_Scalar_Types()
    {
        var document = SpecFixture.ParseSchemas("""
        {
          "A": { "type": "string" },
          "B": { "type": "number" },
          "C": { "type": "integer" },
          "D": { "type": "boolean" }
        }
        """);

        await Assert.That(((PrimitiveNode)document.Schemas["A"]).Kind).IsEqualTo(PrimitiveKind.String);
        await Assert.That(((PrimitiveNode)document.Schemas["B"]).Kind).IsEqualTo(PrimitiveKind.Number);
        await Assert.That(((PrimitiveNode)document.Schemas["C"]).Kind).IsEqualTo(PrimitiveKind.Integer);
        await Assert.That(((PrimitiveNode)document.Schemas["D"]).Kind).IsEqualTo(PrimitiveKind.Boolean);
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Schema_Uses_AllOf()
    {
        var ex = await Assert.That(() => SpecFixture.ParseSchemas(
            """{ "Bad": { "allOf": [ { "type": "string" } ] } }"""))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("allOf");
        await Assert.That(ex.Message).Contains("Bad");
    }

    // Same shapes for the remaining cases:
    // Parse_Should_Record_Format_On_Primitive_Nodes
    //   { "Bin": { "type": "string", "format": "binary" } } → Format == "binary"
    // Parse_Should_Ignore_Validation_Keywords
    //   { "Id": { "type": "string", "pattern": "^ses" },
    //     "Seq": { "type": "integer", "minimum": 0, "exclusiveMinimum": 0, "maximum": 10 },
    //     "Batch": { "type": "array", "minItems": 1, "items": { "type": "string" } } } → all parse
    // Parse_Should_Keep_Dotted_Schema_Names_Verbatim
    //   { "session.status": { "type": "string" } } → Schemas key "session.status" (§11 dotted-name quirk)
    // Parse_Should_Record_Description_On_Nodes
    //   { "Doc": { "type": "string", "description": "documented" } } → Description set
    // Parse_Should_Produce_Enum_Node_For_Multi_Value_String_Enum
    //   { "Kind": { "type": "string", "enum": ["file", "directory"] } } → EnumNode values in order
    // Parse_Should_Produce_Array_Node_With_Item
    //   { "Names": { "type": "array", "items": { "type": "string" } } } → ArrayNode(Item: PrimitiveNode)
    // Parse_Should_Produce_Ref_Node_For_Component_Ref
    //   { "Target": { "type": "string" }, "Alias": { "$ref": "#/components/schemas/Target" } }
    //   → RefNode Target == "Target"
    // Parse_Should_Promote_Inline_Enum_Under_Array_Items
    //   { "Levels": { "type": "array", "items": { "type": "string", "enum": ["low", "high"] } } }
    //   → Schemas["Levels"] is ArrayNode whose Item is RefNode("Levels#/items");
    //     Schemas["Levels#/items"] is EnumNode
    // Parse_Should_Refuse_When_Schema_Uses_Discriminator
    //   { "Bad": { "type": "object", "discriminator": { "propertyName": "type" } } } → "discriminator"
    //   (the error must fire on 'discriminator' even though 'object' parsing lands in task 3)
    // Parse_Should_Refuse_When_Type_Is_An_Array
    //   { "Bad": { "type": ["string", "null"] } } → message contains "type array" wording
    // Parse_Should_Refuse_When_Keyword_Is_Unknown
    //   { "Bad": { "type": "string", "x-custom": true } } → "x-custom"
    // Parse_Should_Refuse_When_Ref_Points_Outside_Component_Schemas
    //   { "Bad": { "$ref": "#/components/responses/X" } } → "#/components/responses/X"
    // Parse_Should_Refuse_When_Ref_Target_Is_Missing
    //   { "Bad": { "$ref": "#/components/schemas/Ghost" } } → "Ghost"
    // Parse_Should_Refuse_When_Array_Has_No_Items
    //   { "Bad": { "type": "array" } } → "items"
    // Parse_Should_Batch_Multiple_Errors
    //   { "BadA": { "allOf": [] }, "BadB": { "type": "array" } } → Message contains "BadA" and "BadB"
}
```

Write every listed test in full — the comment block above is the case inventory, not an
excuse to skip; each becomes a real `[Test]` method following the two shown patterns.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests`
Expected: build FAILS with CS0246 (`PrimitiveNode` etc. not found).

- [ ] **Step 3: Implement** — the node records (each mirroring the `SchemaNode` base:
`Children` returns `[]` for leaves, `[Item]` for `ArrayNode`), `SchemaNodeParser` with
the rule table above, the `ReadSchemas` wiring, and the dangling-ref sweep. Keep each
dispatch branch a small private method (MA0051).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests`
Expected: PASS (task 1 suite + all task 2 tests).

- [ ] **Step 5: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 6: Commit**

```bash
git add tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): schema node core with keyword wall and inline promotion"
```

---

### Task 3: Object family — object, dictionary, free-form, hybrid `additionalProperties`

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/ObjectNode.cs`, `SpecProperty.cs`,
  `AdditionalPropertiesKind.cs`, `DictionaryNode.cs`, `FreeFormObjectNode.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SchemaNodeParser.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecParserObjectNodeTests.cs`

**Interfaces:**
- Consumes: task 2's `SchemaNodeParser` dispatch, node records, promotion machinery
  (`{root}#{pointer}` keys), `SpecFixture.ParseSchemas`.
- Produces:
  - `public sealed record SpecProperty { required string Name; required SchemaNode Schema; required bool IsRequired; }`
  - `public sealed record ObjectNode : SchemaNode { required IReadOnlyList<SpecProperty> Properties; required AdditionalPropertiesKind AdditionalProperties; SchemaNode? AdditionalPropertiesSchema; }`
    — `Properties` in document order; `Children` = property schemas + the AP schema when
    present. (`LiteralMarkers` arrives in task 4, `ErrorStyle` in task 5.)
  - `public enum AdditionalPropertiesKind { Open, Forbidden, Schema }`
  - `public sealed record DictionaryNode : SchemaNode { required SchemaNode Value; }`
  - `public sealed record FreeFormObjectNode : SchemaNode;`
  - `ObjectNode` becomes promotion-eligible (inline objects register under
    `{root}#{pointer}` and are replaced by a `RefNode`).

Dispatch rules for `type: "object"`:
- `properties` present ⇒ `ObjectNode`. Property-bag keys are **opaque wire names** — a
  property named `type`, `required`, `properties`, or `additionalProperties` is data,
  never a keyword (the `GlobalEvent` lesson). `required` array names must each match a
  property (else refuse — verified safe against the pinned spec); property order is
  document order. `additionalProperties`: absent ⇒ `Open`; `false` ⇒ `Forbidden`;
  schema ⇒ `Schema` + parsed node at `{pointer}/additionalProperties` (hybrid objects —
  six exist in the pinned spec); `true` ⇒ refuse.
- no `properties`, `additionalProperties` is a schema ⇒ `DictionaryNode` (value node at
  `{pointer}/additionalProperties`).
- no `properties`, no `additionalProperties`, `patternProperties` with exactly one
  pattern entry ⇒ `DictionaryNode` (value node at `{pointer}/patternProperties`; the key
  pattern is validation-only and dropped — the `v2.session.active` payload, the
  document's only occurrence). Multiple patterns, or `patternProperties` combined with
  `properties`/`additionalProperties`, refuse.
- no `properties`, no `additionalProperties`, no `patternProperties` ⇒
  `FreeFormObjectNode` (161 sites in the pinned spec, e.g. `Session.metadata`).
- `properties: {}` (empty) ⇒ `ObjectNode` with zero properties (the `GlobalEvent` payload
  branches).

- [ ] **Step 1: Write the failing tests** — first exemplar in full, then the case
inventory (write each as a real `[Test]` method with the same fixture/assert idiom):

```csharp
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserObjectNodeTests
{
    [Test]
    public async Task Parse_Should_Produce_Object_Node_With_Properties_And_Required_Set()
    {
        var document = SpecFixture.ParseSchemas("""
        {
          "Session": {
            "type": "object",
            "properties": { "id": { "type": "string" }, "title": { "type": "string" } },
            "required": ["id"],
            "additionalProperties": false
          }
        }
        """);

        var node = (ObjectNode)document.Schemas["Session"];
        await Assert.That(node.Properties.Select(p => p.Name)).IsEquivalentTo(["id", "title"]);
        await Assert.That(node.Properties.Single(p => p.Name == "id").IsRequired).IsTrue();
        await Assert.That(node.Properties.Single(p => p.Name == "title").IsRequired).IsFalse();
        await Assert.That(node.AdditionalProperties).IsEqualTo(AdditionalPropertiesKind.Forbidden);
    }
}
```

```text
Parse_Should_Promote_Inline_Object_Property
  { "Parent": { "type": "object",
      "properties": { "child": { "type": "object",
        "properties": { "x": { "type": "string" } },
        "additionalProperties": false } },
      "additionalProperties": false } }
  → Parent's child property Schema is RefNode("Parent#/properties/child");
    Schemas["Parent#/properties/child"] is ObjectNode
Parse_Should_Classify_Missing_Additional_Properties_As_Open
Parse_Should_Keep_Property_Schema_For_Hybrid_Objects
  (properties + additionalProperties schema → Schema kind + both populated)
Parse_Should_Produce_Dictionary_Node_When_Only_Additional_Properties_Schema
  { "Env": { "type": "object", "additionalProperties": { "type": "string" } } }
Parse_Should_Produce_Dictionary_Node_For_Pattern_Properties
  the verbatim v2.session.active payload interior:
  { "Active": { "type": "object",
      "patternProperties": { "^ses": { "$ref": "#/components/schemas/Target" } } },
    "Target": { "type": "object", "properties": {}, "additionalProperties": false } }
  → DictionaryNode(Value: RefNode("Target"))
Parse_Should_Refuse_When_Pattern_Properties_Has_Multiple_Patterns
Parse_Should_Refuse_When_Pattern_Properties_Combined_With_Properties
Parse_Should_Produce_Free_Form_Node_For_Bare_Object
  { "Meta": { "type": "object" } }
Parse_Should_Produce_Empty_Object_Node_When_Properties_Is_Empty
  { "Unit": { "type": "object", "properties": {}, "additionalProperties": false } }
Parse_Should_Treat_Keyword_Named_Properties_As_Plain_Names
  { "Evt": { "type": "object",
      "properties": {
        "type": { "type": "string" },
        "properties": { "type": "object", "properties": {}, "additionalProperties": false },
        "required": { "type": "boolean" } },
      "required": ["type", "properties"], "additionalProperties": false } }
  → three properties with those wire names; no wall error
Parse_Should_Refuse_When_Additional_Properties_Is_True
Parse_Should_Refuse_When_Required_Names_Missing_Property
  (required: ["ghost"] with no such property → "ghost")
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test tests/OpenCode.Sdk.Tools.Tests`,
CS0246 for the new node types.

- [ ] **Step 3: Implement** the three node records + dispatch extension.

- [ ] **Step 4: Run to verify they pass** — same command, full suite green.

- [ ] **Step 5: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 6: Commit**

```bash
git add tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): object, dictionary and free-form schema nodes"
```

---

### Task 4: Literal markers (both dialects), unions, nullable, duplicate-ref dedup

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/LiteralNode.cs`, `LiteralKind.cs`,
  `LiteralDialect.cs`, `LiteralMarker.cs`, `UnionNode.cs`, `UnionKeyword.cs`,
  `NullableNode.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SchemaNodeParser.cs`, `ObjectNode.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecParserUnionTests.cs`

**Interfaces:**
- Consumes: tasks 2–3 (`SchemaNodeParser`, `ObjectNode`, promotion, `RefNode`).
- Produces:
  - `public sealed record LiteralNode : SchemaNode { required LiteralKind Kind; required string Value; required LiteralDialect Dialect; }`
    — `Value` is the literal's JSON text (`"text"`, `"true"`, `"false"`);
    `public enum LiteralKind { String, Boolean }`;
    `public enum LiteralDialect { SingleValueEnum, Const }`.
  - `public sealed record LiteralMarker { required string PropertyName; required LiteralKind Kind; required string Value; }`
  - `ObjectNode` gains `required IReadOnlyList<LiteralMarker> LiteralMarkers` — computed
    mechanically at construction: required properties whose schema is a `LiteralNode`, in
    property order (never a hardcoded name list — generator spec §4.1).
  - `public sealed record UnionNode : SchemaNode { required IReadOnlyList<SchemaNode> Branches; required UnionKeyword Keyword; }`
    with `public enum UnionKeyword { AnyOf, OneOf }`.
  - `public sealed record NullableNode : SchemaNode { required SchemaNode Inner; }`
  - Promotion: inline union **branches** that become `ObjectNode`s register under
    `{root}#{pointer}/anyOf/{branch}` (or `/oneOf/`) where `{branch}` is
    `{prop}={value}` from the branch's alphabetically-first literal marker, else the
    ordinal index. `UnionNode` itself is promotion-eligible like `ObjectNode`/`EnumNode`.

Dispatch extensions:
- `enum` with exactly one value ⇒ `LiteralNode` (dialect `SingleValueEnum`); values may
  be string (`{"type":"string","enum":["oauth"]}`) or boolean
  (`{"type":"boolean","enum":[true]}`). Boolean `enum` with two or more values ⇒ refuse.
- `const` present ⇒ `LiteralNode` (dialect `Const`) — the observed newer dialect
  (research doc 09); string and boolean consts accepted; `enum`+`const` together ⇒ refuse.
- `anyOf`/`oneOf` (both together ⇒ refuse) — locked analysis order:
  1. *(task 5 prepends the special-value-number check here)*
  2. **Dedup**: raw `$ref` branches with the same target collapse to the first
     occurrence (26 duplicated-ref sites in the pinned spec, e.g. `v2.session.list` 400:
     `[InvalidCursorError, InvalidRequestError, InvalidRequestError]`).
  3. **Null extraction**: branches that are exactly `{"type":"null"}` (± `description`)
     are removed; if any were present the result is wrapped in `NullableNode`.
  4. One branch left ⇒ that branch's node, plain ("a post-dedup single-ref `anyOf` is a
     plain ref"). Zero branches left ⇒ refuse.
  5. Else ⇒ `UnionNode` with the keyword recorded. Branches are arbitrary nodes —
     primitive and literal branches occur (`ProviderConfig` `timeout`:
     `anyOf [number, {"type":"boolean","enum":[false]}]`).
- `{"type":"null"}` outside a union position ⇒ refuse.

- [ ] **Step 1: Write the failing tests** — first exemplar in full, then the case
inventory (write each as a real `[Test]` method with the same fixture/assert idiom):

```csharp
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserUnionTests
{
    [Test]
    public async Task Parse_Should_Produce_Literal_Node_For_Single_Value_Enum()
    {
        var document = SpecFixture.ParseSchemas(
            """{ "OAuthKind": { "type": "string", "enum": ["oauth"] } }""");

        var literal = (LiteralNode)document.Schemas["OAuthKind"];
        await Assert.That(literal.Kind).IsEqualTo(LiteralKind.String);
        await Assert.That(literal.Value).IsEqualTo("oauth");
        await Assert.That(literal.Dialect).IsEqualTo(LiteralDialect.SingleValueEnum);
    }
}
```

```text
Parse_Should_Produce_Literal_Node_For_Const
  { "Marker": { "type": "string", "const": "text" } }
  → LiteralNode(String, "text", Const)   [the newer-dialect fixture — 0 const in the pinned spec]
Parse_Should_Produce_Literal_Node_For_Boolean_Enum
  { "Healthy": { "type": "boolean", "enum": [true] } } → LiteralNode(Boolean, "true", SingleValueEnum)
Parse_Should_Collect_Literal_Markers_On_Object_Nodes
  object with required "type" literal + required "id" string + optional "status" literal
  → LiteralMarkers == [type], not id (not literal), not status (not required)
Parse_Should_Produce_Union_Node_For_AnyOf
  { "Part": { "anyOf": [ { "$ref": "#/components/schemas/A" }, { "$ref": "#/components/schemas/B" } ] },
    "A": {...marker object...}, "B": {...marker object...} }
  → UnionNode(AnyOf, 2 ref branches)
Parse_Should_Produce_Union_Node_For_OneOf
  same shape with oneOf → UnionNode(OneOf)   [the SessionDurableEvent finding]
Parse_Should_Parse_Nested_Unions
  a marker-object branch whose property is itself an anyOf of two marker objects
  (the ToolState pattern) → outer union branch resolves (via promoted refs) to an object
  whose property is a promoted inner UnionNode
Parse_Should_Promote_Union_Branches_With_Marker_Keys
  { "Evt": { "anyOf": [
      { "type": "object", "properties": { "type": { "type": "string", "enum": ["created"] } },
        "required": ["type"], "additionalProperties": false },
      { "type": "object", "properties": { "type": { "type": "string", "enum": ["deleted"] } },
        "required": ["type"], "additionalProperties": false } ] } }
  → Schemas contains "Evt#/anyOf/type=created" and "Evt#/anyOf/type=deleted"
Parse_Should_Wrap_Nullable_When_AnyOf_Has_Null_Branch
  { "Project": { "anyOf": [ { "$ref": "#/components/schemas/Summary" }, { "type": "null" } ] },
    "Summary": { "type": "object", "properties": {}, "additionalProperties": false } }
  → NullableNode(Inner: RefNode("Summary"))
Parse_Should_Dedup_Duplicate_Refs_In_AnyOf
  anyOf [A, B, B] → UnionNode with branch targets [A, B]
Parse_Should_Collapse_To_Plain_Ref_When_Dedup_Leaves_One_Branch
  anyOf [A, A] → RefNode("A") directly (no UnionNode)
Parse_Should_Union_Primitive_And_Literal_Branches
  { "Timeout": { "anyOf": [ { "type": "number" },
      { "type": "boolean", "enum": [false] } ] } }
  → UnionNode [PrimitiveNode(Number), LiteralNode(Boolean "false")]
  (two branches — not the special-value-number shape, which needs NaN/Infinity literals)
Parse_Should_Refuse_Multi_Value_Boolean_Enum
  { "Bad": { "type": "boolean", "enum": [true, false] } } → refuse
```

- [ ] **Step 2: Run to verify they fail** — CS0246 for `LiteralNode` etc.

- [ ] **Step 3: Implement.** Note `ObjectNode.LiteralMarkers` is a new `required` member —
tasks 2–3 construct `ObjectNode` only inside `SchemaNodeParser`, so the only construction
site updates; earlier tests keep passing untouched.

- [ ] **Step 4: Run to verify they pass** — full suite green.

- [ ] **Step 5: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 6: Commit**

```bash
git add tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): literal markers, unions, nullable wrapping and duplicate-ref dedup"
```

---

### Task 5: Special-value numbers, tuples, JSON-string nodes, error styles

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecialNumberNode.cs`,
  `TupleNode.cs`, `JsonStringNode.cs`, `ErrorStyle.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SchemaNodeParser.cs`, `ObjectNode.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecParserQuirkNodeTests.cs`

**Interfaces:**
- Consumes: task 4's union analysis pipeline (the special-number check slots in as its
  step 1), task 3's `ObjectNode` construction site.
- Produces:
  - `public sealed record SpecialNumberNode : SchemaNode;` — the JSON projection of a JS
    number; slice 3 emits it as `double` +
    `JsonNumberHandling.AllowNamedFloatingPointLiterals`.
  - `public sealed record TupleNode : SchemaNode { required IReadOnlyList<SchemaNode> Items; }`
  - `public sealed record JsonStringNode : SchemaNode { required SchemaNode Inner; }`
  - `public enum ErrorStyle { None, EffectTag, NameData }` and `ObjectNode` gains
    `required ErrorStyle ErrorStyle` — computed at construction:
    required `_tag` literal-marker property ⇒ `EffectTag`; else required `name`
    literal-marker **and** required `data` property ⇒ `NameData`; else `None`
    (structural facts, never name-list driven beyond these two wire conventions —
    generator spec §4.1 / ADR-0007).

Dispatch extensions:
- **Special-value number** (union analysis step 1): an `anyOf` where exactly one branch
  is `{"type":"number"}` and every other branch is `{"type":"string"}` with an `enum`
  whose values ⊆ {`NaN`, `Infinity`, `-Infinity`} ⇒ `SpecialNumberNode`. The pinned
  spec's shape has five branches — three single-literal plus one **multi-literal**
  branch `["Infinity","-Infinity","NaN"]`; the subset rule absorbs both. Any other
  branch composition falls through to the generic union path unchanged.
- **Tuple**: `type: "array"` + `prefixItems` ⇒ `TupleNode` (items recursed at
  `{pointer}/prefixItems/{index}`); `items` + `prefixItems` together ⇒ refuse; when
  `minItems`/`maxItems` accompany `prefixItems` they must equal the `prefixItems` count
  ⇒ else refuse. (On plain arrays they stay known-ignored validation keywords — task 2.)
- **JSON-string**: `type: "string"` + `contentSchema` ⇒ `JsonStringNode`;
  `contentMediaType` must be present and `application/json` ⇒ else refuse; inner schema
  recursed at `{pointer}/contentSchema`.

- [ ] **Step 1: Write the failing tests** — first exemplar in full, then the case
inventory (write each as a real `[Test]` method with the same fixture/assert idiom):

```csharp
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserQuirkNodeTests
{
    [Test]
    public async Task Parse_Should_Classify_Special_Value_Number()
    {
        var document = SpecFixture.ParseSchemas("""
        {
          "TimeUsed": {
            "anyOf": [
              { "type": "number" },
              { "type": "string", "enum": ["NaN"] },
              { "type": "string", "enum": ["Infinity"] },
              { "type": "string", "enum": ["-Infinity"] },
              { "type": "string", "enum": ["Infinity", "-Infinity", "NaN"] }
            ]
          }
        }
        """);

        await Assert.That(document.Schemas["TimeUsed"]).IsTypeOf<SpecialNumberNode>();
    }
}
```

(The fixture is the verbatim `Workspace.timeUsed` shape — including the multi-literal
fifth branch the subset rule must absorb.)

```text
Parse_Should_Not_Classify_Special_Value_Number_When_Extra_Branch_Present
  anyOf [number, {"type":"string","enum":["NaN"]}, {"type":"string"}] → UnionNode (not special)
Parse_Should_Produce_Tuple_Node_For_Prefix_Items
  the verbatim Config.plugin shape: { "type": "array",
    "prefixItems": [ { "type": "string" }, { "type": "object" } ],
    "minItems": 2, "maxItems": 2 }
  → TupleNode [PrimitiveNode(String), FreeFormObjectNode]
Parse_Should_Refuse_When_Tuple_Arity_Conflicts_With_Min_Max
  prefixItems ×2 with maxItems 3 → refuse
Parse_Should_Refuse_When_Items_And_Prefix_Items_Coexist
Parse_Should_Produce_Json_String_Node_For_Content_Schema
  { "Stream": { "type": "string", "contentMediaType": "application/json",
      "contentSchema": { "$ref": "#/components/schemas/Evt" } }, "Evt": {...} }
  → JsonStringNode(Inner: RefNode("Evt"))
Parse_Should_Refuse_When_Content_Media_Type_Is_Not_Json
  contentMediaType "text/plain" → refuse
Parse_Should_Detect_Effect_Tag_Error_Style
  { "BadRequest": { "type": "object",
      "properties": { "_tag": { "type": "string", "enum": ["BadRequest"] } },
      "required": ["_tag"], "additionalProperties": false } } → EffectTag
Parse_Should_Detect_Name_Data_Error_Style
  the verbatim MoveSessionError shape (name literal + data object, both required) → NameData
Parse_Should_Leave_Error_Style_None_For_Plain_Objects
```

- [ ] **Step 2: Run to verify they fail** — CS0246 for the new node types.

- [ ] **Step 3: Implement.** `ErrorStyle` is again a new `required` member on
`ObjectNode` with a single construction site.

- [ ] **Step 4: Run to verify they pass** — full suite green.

- [ ] **Step 5: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 6: Commit**

```bash
git add tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): special-value numbers, tuples, JSON-string nodes and error styles"
```

---

### Task 6: `SpecMediaType` — parameter-stripped media types

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecMediaType.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecMediaTypeTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (pure value type).
- Produces:
  - `public sealed record SpecMediaType { required string Raw; required string Stripped; required bool IsJson; required bool IsEventStream; }`
  - `public static SpecMediaType Create(string raw)` — guards null/whitespace
    (`ArgumentException.ThrowIfNullOrWhiteSpace`); `Stripped` = the `type/subtype` before
    the first `;`, trimmed, lowercased with `ToLowerInvariant`; no `/` in the stripped
    value ⇒ `ArgumentException`. `IsJson` = stripped equals `application/json` or subtype
    ends with `+json` (ordinal). `IsEventStream` = stripped equals `text/event-stream`.
    Every downstream match runs on `Stripped` (generator spec §4.1 — upstream's
    `isContentType` compares the same way); `Raw` is recorded for wire fidelity.

- [ ] **Step 1: Write the failing tests** (SUT is `SpecMediaType` — direct tests):

```csharp
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecMediaTypeTests
{
    [Test]
    public async Task Create_Should_Strip_Parameters()
    {
        var media = SpecMediaType.Create("text/x-diff; charset=utf-8");

        await Assert.That(media.Raw).IsEqualTo("text/x-diff; charset=utf-8");
        await Assert.That(media.Stripped).IsEqualTo("text/x-diff");
        await Assert.That(media.IsJson).IsFalse();
        await Assert.That(media.IsEventStream).IsFalse();
    }

    [Test]
    public async Task Create_Should_Detect_Json()
    {
        await Assert.That(SpecMediaType.Create("application/json").IsJson).IsTrue();
    }

    [Test]
    public async Task Create_Should_Detect_Json_Suffix()
    {
        await Assert.That(SpecMediaType.Create("application/problem+json").IsJson).IsTrue();
    }

    [Test]
    public async Task Create_Should_Detect_Event_Stream()
    {
        await Assert.That(SpecMediaType.Create("text/event-stream").IsEventStream).IsTrue();
    }

    [Test]
    public async Task Create_Should_Lowercase_Stripped_Value()
    {
        await Assert.That(SpecMediaType.Create("Application/JSON").Stripped)
            .IsEqualTo("application/json");
    }

    [Test]
    public async Task Create_Should_Throw_When_Media_Type_Is_Malformed()
    {
        await Assert.That(() => SpecMediaType.Create("no-slash")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Create_Should_Throw_When_Media_Type_Is_Blank()
    {
        await Assert.That(() => SpecMediaType.Create(" ")).Throws<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run to verify they fail** — CS0246.

- [ ] **Step 3: Implement** per the rules above.

- [ ] **Step 4: Run to verify they pass.**

- [ ] **Step 5: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 6: Commit**

```bash
git add tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): media type parsing with parameter stripping"
```

---

### Task 7: Operation skeleton — surface split, method/path wall, extension dispositions

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecSurface.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecOperation.cs`, `SpecParser.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecParserOperationTests.cs`

**Interfaces:**
- Consumes: task 1's document walk (`paths` was accepted but unparsed until now),
  `SpecParseErrorCollector`, `SpecFixture.ParsePaths`.
- Produces — `SpecOperation` gains its identity members (tasks 8–9 add
  `Parameters`/`RequestBody`/`Responses`/`IsSse`):
  - `public enum SpecSurface { Modern, Legacy }`
  - ```csharp
    public sealed record SpecOperation
    {
        public required string OperationId { get; init; }
        public required SpecSurface Surface { get; init; }
        public required IReadOnlyList<string> Segments { get; init; }
        public required string Method { get; init; }        // wire verb: "get"
        public required string Path { get; init; }          // raw template, incl. trailing *
        public required bool HasWildcardPath { get; init; }
        public required bool IsWebSocket { get; init; }
        public required bool IsDeprecated { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
    }
    ```
  - `SpecDocument.Operations` now populated, document order (paths order, then method
    order within a path item).

Parsing rules:
- Path-item keys must be `get`/`put`/`post`/`delete`/`patch` — anything else (including
  `parameters`, `$ref`, other methods) refuses.
- Operation-key wall per the locked table: unknown keys refuse; `tags`/`security`/
  `x-codeSamples` ignored; `summary`/`description` recorded; `deprecated` (bool) →
  `IsDeprecated`; `x-websocket` must be literal `true` when present → `IsWebSocket`.
  In this task `parameters`/`requestBody`/`responses` are **known but deferred** — the
  wall accepts the keys and tasks 8–9 parse them. Any transitional code comment must
  describe the status quo ("accepted by the wall; not parsed into the IR"), never
  reference plan tasks (Documentation Hygiene: code never cites docs).
- `operationId`: required, non-empty; split on `.`; head `v2` ⇒ `Modern` with the head
  stripped, else `Legacy` with all segments kept — surface is *never* keyed on the path
  (3 modern ops live under `/experimental/`); empty segment list after strip ⇒ refuse.
  Duplicate operationId across the document ⇒ refuse (defensive; pinned spec is unique).
- Path: `HasWildcardPath` = path ends with `/*` (the `/api/fs/read/*` shape); any other
  `*` placement refuses.

- [ ] **Step 1: Write the failing tests** — first exemplar in full, then the case
inventory (write each as a real `[Test]` method with the same fixture/assert idiom).
`ParsePaths` fixtures need a `responses` key on every op to stay real — use a minimal
`"responses": { "204": { "description": "ok" } }` until task 9 parses it:

```csharp
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserOperationTests
{
    [Test]
    public async Task Parse_Should_Split_Modern_Surface_And_Segments()
    {
        var document = SpecFixture.ParsePaths("""
        {
          "/api/session": {
            "get": {
              "operationId": "v2.session.list",
              "responses": { "204": { "description": "ok" } }
            }
          }
        }
        """);

        var operation = document.Operations.Single();
        await Assert.That(operation.OperationId).IsEqualTo("v2.session.list");
        await Assert.That(operation.Surface).IsEqualTo(SpecSurface.Modern);
        await Assert.That(operation.Segments).IsEquivalentTo(["session", "list"]);
        await Assert.That(operation.Method).IsEqualTo("get");
        await Assert.That(operation.Path).IsEqualTo("/api/session");
    }
}
```

```text
Parse_Should_Keep_Legacy_Segments_Intact
  op "session.get" → Legacy, Segments ["session","get"]
Parse_Should_Record_Deep_Group_Segments
  op "v2.session.permissions.respond" → Segments ["session","permissions","respond"]
Parse_Should_Flag_Wildcard_Path
  path "/api/fs/read/*" → HasWildcardPath true, Path verbatim
Parse_Should_Record_Summary_Description_And_Deprecated
Parse_Should_Record_WebSocket_Flag        ("x-websocket": true → IsWebSocket)
Parse_Should_Ignore_Tags_Security_And_Code_Samples
  (op carries all three → parses clean)
Parse_Should_Refuse_When_Operation_Id_Is_Missing
Parse_Should_Refuse_When_Operation_Id_Is_Duplicated   (two paths, same operationId)
Parse_Should_Refuse_When_Method_Is_Unknown            ("options" path-item key)
Parse_Should_Refuse_When_Wildcard_Is_Not_Trailing     (path "/api/*/read")
Parse_Should_Refuse_When_Operation_Key_Is_Unknown     ("x-madeup": true)
Parse_Should_List_Operations_In_Document_Order        (two paths → order preserved)
```

- [ ] **Step 2: Run to verify they fail** — the new asserts fail to compile against the
empty `SpecOperation` shell (CS1061).

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run to verify they pass** — full suite green (task 1's empty-document
test keeps passing: no paths ⇒ no operations).

- [ ] **Step 5: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 6: Commit**

```bash
git add tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): operation parsing with surface split and dialect wall"
```

---

### Task 8: Parameters and request bodies

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecParameter.cs`,
  `SpecParameterLocation.cs`, `SpecRequestBody.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecOperation.cs`, `SpecParser.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecParserParameterTests.cs`

**Interfaces:**
- Consumes: task 7's operation walk, task 2's `SchemaNodeParser` (parameter/body schemas
  parse through it under root `op:{operationId}`), task 6's `SpecMediaType.Create`.
- Produces:
  - `public enum SpecParameterLocation { Path, Query, Header }`
  - `public sealed record SpecParameter { required string Name; required SpecParameterLocation Location; required SchemaNode Schema; required bool IsRequired; required bool IsDeepObject; }`
  - `public sealed record SpecRequestBody { required SpecMediaType ContentType; required SchemaNode Schema; required bool IsRequired; }`
  - `SpecOperation` gains `required IReadOnlyList<SpecParameter> Parameters` and
    `SpecRequestBody? RequestBody`.

Parsing rules:
- Parameter keys per the locked wall; `in` maps `path`/`query`/`header` (anything else —
  e.g. `cookie` — refuses); `required` defaults to false (the pinned spec omits it on 8
  pty parameters); names are verbatim wire names — bracketed names like
  `location[directory]` pass through untouched.
- `style`/`explode`: only the pair `deepObject`+`true` is known ⇒ `IsDeepObject`; either
  key alone, or any other value, refuses.
- Parameter schema parses through `SchemaNodeParser` at pointer `/parameters/{name}` —
  the legacy boolean-ish `anyOf [boolean, {"type":"string","enum":["true","false"]}]`
  parameter shape lands as a `UnionNode` mechanically; object schemas (deepObject) get
  promoted like any inline object.
- Duplicate (name, in) pair within one operation ⇒ refuse (defensive).
- **Path-template cross-check** (both directions, per operation): every `{token}` in the
  path template must have a declared `path` parameter of that name; every declared `path`
  parameter must appear in the template. The trailing wildcard `*` segment is not a token.
- `requestBody`: keys `content`/`required` only; exactly one media entry (zero or
  several ⇒ refuse); media-object key `schema` only; content type through
  `SpecMediaType.Create` (a malformed value's `ArgumentException` is caught and converted
  to a collector error); schema parsed at pointer `/requestBody`.

- [ ] **Step 1: Write the failing tests** — first exemplar in full, then the case
inventory (write each as a real `[Test]` method with the same fixture/assert idiom):

```csharp
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserParameterTests
{
    [Test]
    public async Task Parse_Should_Record_Path_And_Query_Parameters()
    {
        var document = SpecFixture.ParsePaths("""
        {
          "/api/session/{sessionID}/history": {
            "get": {
              "operationId": "v2.session.history",
              "parameters": [
                { "name": "sessionID", "in": "path",
                  "schema": { "type": "string", "pattern": "^ses" }, "required": true },
                { "name": "limit", "in": "query", "schema": { "type": "string" } },
                { "name": "after", "in": "query", "schema": { "type": "string" } }
              ],
              "responses": { "204": { "description": "ok" } }
            }
          }
        }
        """);

        var parameters = document.Operations.Single().Parameters;
        var sessionId = parameters.Single(p => p.Name == "sessionID");
        await Assert.That(sessionId.Location).IsEqualTo(SpecParameterLocation.Path);
        await Assert.That(sessionId.IsRequired).IsTrue();
        var limit = parameters.Single(p => p.Name == "limit");
        await Assert.That(limit.Location).IsEqualTo(SpecParameterLocation.Query);
        await Assert.That(limit.IsRequired).IsFalse();
        await Assert.That(limit.IsDeepObject).IsFalse();
    }
}
```

```text
Parse_Should_Flag_Deep_Object_Parameters
  the verbatim v2.fs.read location parameter (deepObject + explode true, object schema)
  → IsDeepObject true; Schema is a promoted ref; Schemas["op:v2.fs.read#/parameters/location"]
    is ObjectNode
Parse_Should_Keep_Bracketed_Parameter_Names_Verbatim
  query params "location[directory]", "location[workspace]" → names verbatim
Parse_Should_Parse_Parameter_Schema_Through_Node_Parser
  the boolean-ish anyOf parameter → UnionNode branches [PrimitiveNode(Boolean), EnumNode]
Parse_Should_Record_Request_Body_With_Media_Type
  post op, requestBody required true, application/json, inline object schema
  → ContentType.IsJson, IsRequired true, Schema promoted at "op:{id}#/requestBody"
Parse_Should_Default_Request_Body_Required_To_False
Parse_Should_Refuse_When_Path_Parameter_Is_Undeclared      (template {id}, no param)
Parse_Should_Refuse_When_Declared_Path_Parameter_Missing_From_Template
Parse_Should_Refuse_When_Parameter_Style_Is_Unknown        (style "form")
Parse_Should_Refuse_When_Parameter_Location_Is_Unknown     (in "cookie")
Parse_Should_Refuse_When_Parameter_Is_Duplicated           (same name+in twice)
Parse_Should_Refuse_When_Request_Body_Has_Multiple_Content_Types
```

- [ ] **Step 2: Run to verify they fail** — CS0246/CS1061 for the new members.

- [ ] **Step 3: Implement.** (Task 7's existing operation tests need their fixture ops
extended only if they asserted member counts — they did not; `Parameters` defaults to
empty list for ops without a `parameters` key.)

- [ ] **Step 4: Run to verify they pass** — full suite green.

- [ ] **Step 5: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 6: Commit**

```bash
git add tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): parameter and request body parsing"
```

---

### Task 9: Responses — SSE detection, opaque `x-effect-stream`, envelope classification

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecResponse.cs`,
  `SpecEnvelopeShape.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Parsing/SpecOperation.cs`, `SpecParser.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecParserResponseTests.cs`

**Interfaces:**
- Consumes: tasks 7–8's operation walk, task 6's `SpecMediaType`, the completed schema
  graph (classification resolves refs — schemas are parsed before paths since task 1).
- Produces:
  - `public enum SpecEnvelopeShape { None, Bare, Data, DataLocation, CursorData, DataHasMore }`
  - ```csharp
    public sealed record SpecResponse
    {
        public required int StatusCode { get; init; }
        public string? Description { get; init; }
        public SpecMediaType? ContentType { get; init; }   // null = no content
        public SchemaNode? Schema { get; init; }
        public required SpecEnvelopeShape EnvelopeShape { get; init; }
        public required bool IsSse { get; init; }
        public JsonElement? EffectStreamMetadata { get; init; }  // opaque clone
    }
    ```
  - `SpecOperation` gains `required IReadOnlyList<SpecResponse> Responses` (sorted
    ascending by status) and `required bool IsSse` (any response `IsSse`).

Parsing rules:
- Response map keys must parse as integers (`"default"` refuses); response object keys:
  `description`/`content` only.
- No `content` ⇒ `ContentType` null, `Schema` null, shape `None` (the 204 family).
- `content` with one media entry: type through `SpecMediaType.Create`; media keys:
  `schema` (parsed at pointer `/responses/{status}`) plus — **only when the media type is
  `text/event-stream`** — `x-effect-stream`, whose value is carried opaque via
  `JsonElement.Clone()` (never schema-parsed; its interior `not`/`anyOf`-null constructs
  must never reach the node parser — generator spec §4.1). `x-effect-stream` on any
  other media type refuses. Two or more media entries refuse.
- `IsSse` = media `IsEventStream`. The SSE media schema itself parses normally (the
  `v2.session.events` inline `{id, event, data}` envelope promotes like any inline
  object; `global.event`/`event.subscribe`/`v2.event.subscribe` are plain refs).
- **Envelope classification** (parse-time normalization; JSON media only): chase
  `RefNode`s through the graph with a visited set (a revisit refuses —
  `circular ref during envelope classification`); if the settled node is an `ObjectNode`,
  exact property-name-set match: `{data}` ⇒ `Data`; `{data, location}` ⇒ `DataLocation`;
  `{data, cursor}` ⇒ `CursorData` (the `SessionsResponse` named-ref case); `{data,
  hasMore}` ⇒ `DataHasMore`; anything else ⇒ `Bare`. Non-object settled nodes ⇒ `Bare`.
  Non-JSON content (SSE, `application/octet-stream`, `text/x-diff`) ⇒ `Bare`. The shape
  is structural; naming the payload is the Binder's job.

- [ ] **Step 1: Write the failing tests** — first exemplar in full, then the case
inventory (write each as a real `[Test]` method with the same fixture/assert idiom;
envelope fixtures mirror the real shapes: `SessionsResponse` verbatim for `CursorData`,
`SessionHistory` verbatim for `DataHasMore`, `v2.agent.list`'s `{data, location}` with a
`LocationInfo` ref, the `v2.session.events` SSE media with `x-effect-stream`):

```csharp
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserResponseTests
{
    [Test]
    public async Task Parse_Should_Classify_Cursor_Envelope_Behind_Named_Ref()
    {
        var document = SpecFixture.ParsePaths("""
        {
          "/api/session": {
            "get": {
              "operationId": "v2.session.list",
              "responses": {
                "200": {
                  "description": "SessionsResponse",
                  "content": {
                    "application/json": {
                      "schema": { "$ref": "#/components/schemas/SessionsResponse" }
                    }
                  }
                }
              }
            }
          }
        }
        """, schemasJson: """
        {
          "SessionsResponse": {
            "type": "object",
            "properties": {
              "data": { "type": "array", "items": { "type": "string" } },
              "cursor": { "type": "object", "properties": {}, "additionalProperties": false }
            },
            "required": ["data", "cursor"],
            "additionalProperties": false
          }
        }
        """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.StatusCode).IsEqualTo(200);
        await Assert.That(response.ContentType!.IsJson).IsTrue();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.CursorData);
        await Assert.That(response.IsSse).IsFalse();
    }
}
```

```text
Parse_Should_Record_Json_Response_With_Status
Parse_Should_Record_No_Content_Response          (204 → None, null ContentType/Schema)
Parse_Should_Sort_Responses_By_Status            (fixture declares 404 before 200)
Parse_Should_Classify_Data_Envelope              (inline {data} object → Data)
Parse_Should_Classify_Data_Location_Envelope
Parse_Should_Classify_Has_More_Envelope_Behind_Named_Ref
Parse_Should_Classify_Other_Object_Shapes_As_Bare    (inline {info, parts})
Parse_Should_Classify_Non_Json_Content_As_Bare
  (200 application/octet-stream, schema {"type":"string","format":"binary"} → Bare,
   ContentType.Stripped "application/octet-stream")
Parse_Should_Detect_Sse_Response_And_Flag_Operation
  (text/event-stream 200 → response IsSse, operation IsSse)
Parse_Should_Carry_Effect_Stream_Metadata_Opaque
  (SSE media with "x-effect-stream": {"encoding":"json","causeSchema":{"not":{}}}
   → EffectStreamMetadata present; raw JSON round-trips; no parse error from the
   interior "not")
Parse_Should_Refuse_When_Response_Has_Multiple_Content_Types
Parse_Should_Refuse_When_Effect_Stream_Appears_On_Json_Media
Parse_Should_Refuse_When_Response_Media_Key_Is_Unknown   ("examples": {})
Parse_Should_Refuse_When_Response_Status_Is_Not_Numeric  ("default")
Parse_Should_Refuse_When_Ref_Chain_Cycles_During_Envelope_Classification
  ("A": {"$ref": ".../B"}, "B": {"$ref": ".../A"}, response schema → ref A)
```

- [ ] **Step 2: Run to verify they fail** — CS0246/CS1061.

- [ ] **Step 3: Implement.** Task 7/8 fixtures used minimal `responses` blocks — they now
parse for real; their assertions are untouched.

- [ ] **Step 4: Run to verify they pass** — full suite green.

- [ ] **Step 5: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 6: Commit**

```bash
git add tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): response parsing with SSE detection and envelope classification"
```

---

### Task 10: Full pinned-spec smoke test

**Files:**
- Modify: `tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj`
- Test: `tests/OpenCode.Sdk.Tools.Tests/SpecParserSmokeTests.cs`

**Interfaces:**
- Consumes: the complete parser (tasks 1–9); the pinned `spec/openapi.json`; the real
  `FileSystem` from `TestableIO.System.IO.Abstractions.Wrappers` (pinned in task 1).
- Produces: the structural gate every future spec refresh runs through. **No count
  assertions** — counts are research-doc facts; count tests would turn every legitimate
  refresh into noise (generator spec §11).

- [ ] **Step 1: Link the pinned spec and reference Wrappers**

In `tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj`:

```xml
  <ItemGroup>
    <PackageReference Include="TestableIO.System.IO.Abstractions.Wrappers"/>
  </ItemGroup>

  <ItemGroup>
    <None Include="../../spec/openapi.json" Link="Fixtures/openapi.json"
          CopyToOutputDirectory="PreserveNewest"/>
  </ItemGroup>
```

- [ ] **Step 2: Write the failing test**

`tests/OpenCode.Sdk.Tools.Tests/SpecParserSmokeTests.cs`:

```csharp
using System.IO.Abstractions;
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserSmokeTests
{
    private static SpecDocument ParsePinnedSpec()
    {
        FileSystem fileSystem = new();
        // IO0006: even Path goes through the abstraction.
        var path = fileSystem.Path.Combine(AppContext.BaseDirectory, "Fixtures", "openapi.json");
        return new SpecParser(fileSystem).Parse(path);
    }

    [Test]
    public async Task Parse_Should_Accept_The_Full_Pinned_Spec()
    {
        var document = ParsePinnedSpec();

        await Assert.That(document.OpenApiVersion).IsEqualTo("3.1.0");
        await Assert.That(document.Operations).IsNotEmpty();
        await Assert.That(document.Operations.Any(o => o.Surface == SpecSurface.Modern)).IsTrue();
        await Assert.That(document.Operations.Any(o => o.Surface == SpecSurface.Legacy)).IsTrue();
    }

    [Test]
    public async Task Parse_Should_Hold_Structural_Invariants_On_The_Pinned_Spec()
    {
        var document = ParsePinnedSpec();

        // Known landmark constructs — existence and shape, never counts.
        var events = document.Operations.Single(o => o.OperationId == "v2.session.events");
        await Assert.That(events.IsSse).IsTrue();
        await Assert.That(events.Responses.Single(r => r.StatusCode == 200).EffectStreamMetadata)
            .IsNotNull();

        var fsRead = document.Operations.Single(o => o.OperationId == "v2.fs.read");
        await Assert.That(fsRead.HasWildcardPath).IsTrue();

        var ptyConnect = document.Operations.Single(o => o.OperationId == "v2.pty.connect");
        await Assert.That(ptyConnect.IsWebSocket).IsTrue();

        await Assert.That(document.Schemas.ContainsKey("session.status")).IsTrue();
        await Assert.That(document.Schemas["SessionDurableEvent"]).IsTypeOf<UnionNode>();
        await Assert.That(((UnionNode)document.Schemas["SessionDurableEvent"]).Keyword)
            .IsEqualTo(UnionKeyword.OneOf);

        var workspace = (ObjectNode)document.Schemas["Workspace"];
        var timeUsed = workspace.Properties.Single(p => p.Name == "timeUsed");
        await Assert.That(timeUsed.Schema).IsTypeOf<SpecialNumberNode>();

        await Assert.That(((ObjectNode)document.Schemas["MoveSessionError"]).ErrorStyle)
            .IsEqualTo(ErrorStyle.NameData);
        await Assert.That(((ObjectNode)document.Schemas["effect_HttpApiError_BadRequest"]).ErrorStyle)
            .IsEqualTo(ErrorStyle.EffectTag);

        var sessionList = document.Operations.Single(o => o.OperationId == "v2.session.list");
        await Assert.That(sessionList.Responses.Single(r => r.StatusCode == 200).EnvelopeShape)
            .IsEqualTo(SpecEnvelopeShape.CursorData);
        var badRequest = sessionList.Responses.Single(r => r.StatusCode == 400);
        var union = (UnionNode)ResolveRef(document, badRequest.Schema!);
        var targets = union.Branches.Cast<RefNode>().Select(b => b.Target).ToList();
        await Assert.That(targets).IsEquivalentTo(["InvalidCursorError", "InvalidRequestError"]);

        var history = document.Operations.Single(o => o.OperationId == "v2.session.history");
        await Assert.That(history.Responses.Single(r => r.StatusCode == 200).EnvelopeShape)
            .IsEqualTo(SpecEnvelopeShape.DataHasMore);

        var agentList = document.Operations.Single(o => o.OperationId == "v2.agent.list");
        await Assert.That(agentList.Responses.Single(r => r.StatusCode == 200).EnvelopeShape)
            .IsEqualTo(SpecEnvelopeShape.DataLocation);

        var active = document.Operations.Single(o => o.OperationId == "v2.session.active");
        var activeBody = (ObjectNode)ResolveRef(
            document, active.Responses.Single(r => r.StatusCode == 200).Schema!);
        var activeData = ResolveRef(document, activeBody.Properties.Single(p => p.Name == "data").Schema);
        await Assert.That(activeData).IsTypeOf<DictionaryNode>();

        await Assert.That(document.Operations.Any(
            o => o.Responses.Any(r => r.EnvelopeShape == SpecEnvelopeShape.None))).IsTrue();
    }

    [Test]
    public async Task Parse_Should_Produce_Identical_Graph_Keys_On_Reparse()
    {
        var first = ParsePinnedSpec().Schemas.Keys.ToList();
        var second = ParsePinnedSpec().Schemas.Keys.ToList();

        await Assert.That(first.SequenceEqual(second, StringComparer.Ordinal)).IsTrue();
    }

    private static SchemaNode ResolveRef(SpecDocument document, SchemaNode node) =>
        node is RefNode reference ? document.Schemas[reference.Target] : node;
}
```

(If a landmark assertion fails because the pinned wire differs from this plan's reading,
that is evidence, not something to patch around — stop and classify per the deviation
protocol.)

- [ ] **Step 3: Run to verify the red state**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests`
Expected: if tasks 1–9 are complete and correct, this may already pass — the honest red
check is transient: temporarily point `ParsePinnedSpec` at a copy with one construct the
wall refuses (e.g. inject `"allOf": []` into a scratch copy under `.scratchpad/`) and
watch it fail, then restore. If instead the *real* spec fails to parse, the failure list
is the finding — fix the parser task at fault (level 0/1) or stop for level 2.

- [ ] **Step 4: Run to verify green** — full suite green against the real pinned spec.

- [ ] **Step 5: Full gate** (Global Constraints — all four commands clean)

- [ ] **Step 6: Commit**

```bash
git add tests/OpenCode.Sdk.Tools.Tests
git commit -m "test(tools): full pinned-spec smoke test"
```

---

### Task 11: Docs pass, push, PR

**Files:**
- Modify: `docs/ROADMAP.md`
- Modify: `docs/research/00-research-log.md` (only if implementation produced findings
  beyond the planning-time ones already recorded — e.g. a wall refusal the plan did not
  predict, an analyzer arbitration, a TUnit/TestableIO API adaptation worth keeping)

**Interfaces:**
- Consumes: the finished slice on `feature/slice-01-parser-specir`.
- Produces: the merged-PR state the slice map's done-definition requires.

- [ ] **Step 1: Update the ROADMAP status line**

In `docs/ROADMAP.md` Status, replace the final sentence
(`…next is the Slice 1 planning cycle for issue #2.`) with:

```text
Slice 1 (parser + SpecIR) has landed: the wire-faithful parser with its dialect wall,
quirk fixtures, and full pinned-spec smoke test live under
`tools/OpenCode.Sdk.Tools/Generator/Parsing/`; `generate` remains a fail-loud stub until
slice 3. Next is the Slice 2 planning cycle for issue #3 (Binder + curation v0).
```

- [ ] **Step 2: Research-log entry (conditional)** — if any deviation fired or an
API/analyzer adaptation is worth keeping, append a session entry in
question→finding→decision format; otherwise skip (the planning-session entry already
covers the dialect findings).

- [ ] **Step 3: Final full gate on the branch** (all four commands — the Slopwatch
baseline must stay at zero entries).

- [ ] **Step 4: Commit and push**

```bash
git add docs/ROADMAP.md docs/research/00-research-log.md
git commit -m "docs: slice 1 docs pass"
git push -u origin feature/slice-01-parser-specir
```

(Drop the research-log path from `git add` if Step 2 was skipped.)

- [ ] **Step 5: Open the PR**

`gh pr create` targeting `master`, title `feat(tools): slice 1 — parser + SpecIR`; body:
what landed (parser, node kinds, normalizations, wall, fixtures, smoke), the honest note
(CLI unchanged — `generate` still stubs; the slice's working software is the parser
library plus its suite), deviations fired (if any, with levels), and
`Closes #2`. All three CI legs must go green; merge needs maintainer approval (slice
issue closes on merge).

---

## Handoff to Slice 2 (Binder + curation v0)

What this slice hands the Binder, and what it deliberately does not:

- **`SpecDocument`** is the Binder's sole spec-side input: `Operations` (document
  order) + `Schemas` (flat, ordinal-sorted keys). No C# names anywhere — name
  computation, handle routing, and every emission decision are Binder work.
- **Graph keys are a stable contract**: wire names for named schemas; `{root}#{pointer}`
  (marker-keyed union branches) for promoted inline types. The Binder computes the
  reachable closure by walking `SchemaNode.Children` / `RefNode.Target` from each
  included operation's parameters, request body, responses, and SSE item schemas.
- **Recorded structural facts the Binder consumes directly**: `SpecEnvelopeShape` per
  response (payload naming + paginator derivation), `ErrorStyle` per object node
  (ADR-0007 mapping), `LiteralMarker` lists (union dispatch analysis), `IsSse` +
  `EffectStreamMetadata` (hand-wired stream endpoints; the opaque element feeds the
  `handwired` fingerprint document in slice 2's §9 work), `IsWebSocket`
  (`v2.pty.connect` exclusion evidence), `SpecMediaType.Stripped` (the
  `contentTypePayloads` coverage check), `Summary`/`Description` on operations and
  `Description` on nodes (XML-doc computation), `IsDeprecated`.
- **Not produced here**: curation loading, coverage checks, reachable-closure
  computation, fingerprints, `EmitPlan`, any emitter, any Writer, any `generate` wiring.
  The CLI is byte-for-byte unchanged from slice 0.
