# Envelope Completion (C2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development
> (recommended) or deniz-process:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Envelope payloads bind through the generator's existing type machinery — list,
dictionary, promoted-inline-object, and represented-nullable payloads are accepted wherever the
ingested `SchemaNode` binds to a supported `TypeReferencePlan`; everything else still fails
closed, with no family-specific exceptions.

**Architecture:** `EnvelopePlan` carries a `TypeReferencePlan` instead of a payload type-name
string. `EnvelopeFacetBinder` delegates payload binding to the existing `TypePlanBinder`;
DTO/envelope/adapter emitters render payload types through the existing `TypeSyntaxEmitter`; the
serializer registry gains typed entries with pinned `TypeInfoPropertyName` for bare container
payloads. A successful nullable payload is distinguished from the error path by response state
(`IsError`), never by treating CLR null as an unset backing field — the non-nullable
`{"data":null}` refusal stands (Q148).

**Tech Stack:** the repository generator (`tools/OpenCode.Sdk.Tools`), Roslyn syntax emission,
System.Text.Json source generation, TUnit on MTP, `GeneratedSourceCompiler` compile probes.

**Spec:** `docs/superpowers/specs/2026-08-26-continuous-protocol-coverage-program-design.md` §6;
refusal partition in `docs/research/21-openapi-projection-fidelity.md` §4; canon
`docs/architecture/protocol-and-generation.md` (model shape, materialization boundary) and
ADR-0004/0013/0014/0017.

## Global Constraints

- Completion gate per task (`docs/engineering/quality-gates.md`): slopwatch, Release build,
  `dotnet format whitespace`/`style --verify-no-changes`, `dotnet test`; every task here is a
  generator task, so each adds the tool `--help` smoke and `generate --verify`.
- Generated output changes only through the generator; never hand-edit generated files.
- **Territorial rule (design §10, session agreement 2026-08-28):** this arc is the sole owner of
  `tools/`, `tools/curation.json`, the profile, and generated output under `src/OpenCode.Sdk`.
  The parallel M4 arc must not touch them; integration commits are serialized by the session
  coordinator.
- **Family-specific array/dictionary/inline exceptions are forbidden** (design §6). The payload
  shape set is defined once, by what `TypePlanBinder` supports; walls stay in the type machinery.
- The cursor-list dialect keeps its item nominal (ADR-0017): `BindCursorListPayload`'s named-item
  wall is out of scope and unchanged. `SpecEnvelopeShape.DataHasMore` stays refused.
- The runtime materialization boundary (ADR-0014) is unchanged: no new runtime validation; the
  DTO `required` + `RespectNullableAnnotations` walls keep enforcing wire presence/nullability.
- Documentation moves in the same commit as the code it describes (canon paragraph, ROADMAP
  profile counts ride their tasks).
- Commits need no AI trailers; never push without an explicit ask. Direct green commits are
  authorized inside this agreed development loop; canonical-document edits (Task 8) need explicit
  maintainer approval of the wording.

## Mechanism facts the tasks argue from (source-verified 2026-08-28, pin `d2ee536c`)

- `EnvelopeFacetBinder` (tools/…/Binding/EnvelopeFacetBinder.cs) refuses today: Bare payload must
  be `RefNode`→named (`BindBarePayload`), Data payload must be a wrapper whose single required
  `data` is `RefNode`→named (`BindDataPayload`), DataLocation `data` must be named or array-of-named
  (`BindDataLocationPayload`). These three walls produce doc 21 §4's 18 refusals
  (8 envelope + 6 success + 4 location-data).
- `EnvelopePlan` (Binding/Models/EnvelopePlan.cs) carries `PayloadTypeName`/`LocationTypeName`
  strings. Consumers: `EnvelopeEmitter`, `EnvelopeDtoEmitter`, `ResponseAdapterEmitter`
  (`EmitSuccessCreation` reads `OpenCodeJsonContext.Default.<readTypeName>` where
  `readTypeName = EnvelopeDtoTypeName ?? PayloadTypeName`), `OperationMethodEmitter`,
  `PaginationFacetBinder` (cursor-list item type), `SpecBinder` (DTO-name collision wall +
  registry composition from `EnvelopeDtoTypeName`).
- `TypePlanBinder` (Binding/TypePlanBinder.cs) already produces
  `NamedTypeReferencePlan`/`ListTypeReferencePlan`/`DictionaryTypeReferencePlan`/
  `SpecialNumberTypeReferencePlan` with `IsNullable` + `JsonNullRepresentation`, handles
  `NullableNode` (in-band carriers stay non-nullable), collapses collapsible structural unions,
  and refuses inline `ObjectNode`/`EnumNode`/`UnionNode` as "inline nominal schema was not
  promoted into the graph". Its errors go to `BindingErrorCategory.Schema` with a pointer subject.
- `TypeSyntaxEmitter.Emit(TypeReferencePlan)` already renders all four plan kinds including
  nullable wrapping. `TypeSyntaxEmitter.EmitNamed(string)` cannot render generic type names — so
  `RegistryEmitter`, which emits `[JsonSerializable(typeof(EmitNamed(name)))]` per
  `RegistryPlan.TypeNames` string, cannot register `IReadOnlyList<X>` today.
- `EnvelopeEmitter`'s payload guard is `_field ?? throw InvalidOperationException` over a
  `NullableType(payloadType)` backing field — a represented-null payload would be
  indistinguishable from the error path, exactly what design §6 forbids.
- STJ source generation covers a registered type's property types transitively: DTO-wrapped
  container payloads need no extra registry entry; only **bare** container payloads need a
  registered generic instantiation plus a deterministic context property name.
- Ingestion promotes inline nominal schemas into the graph under op-scoped keys
  (`op:<operationId>#/<pointer>`; see the `op:` schemaAlias rows in tools/curation.json), and
  `SchemaNameResolver.NormalizeRoot` strips the `op:` prefix when deriving names.
  `ReachableSchemaCollector` excludes only envelope wrapper **roots** (`_responseRoots`), not the
  `data` subtree.
- New at `d2ee536c`: `server.experimental.persistentPty.handoff` answers a **Bare** single-property
  wrapper `{"handoff": PersistentPty.Handoff | null}` (the property is `handoff`, not `data`, so
  the classifier calls it Bare) — an inline-object payload whose *property* is nullable. It stays
  pending with its family; it is admission evidence for Task 5's mechanism, not a selection target.
- Test conventions: synthetic specs via `SpecScenario.Define(...)` +
  `BindingTestHost.IngestAsync(...)` + `new BindingTestHost().Bind(document, Selection(...),
  Curation(...))`; pinned binding via `new BindingTestHost().BindPinnedAsync()`; refusals via
  `AssertOperationRefusalAsync(document, opId, fragment)` (OperationPlanBinderTests.cs); compile
  probes via `GeneratedSourceCompiler.CompileWithSdkCoreAsync`/`CompileAndLoadWithSdkCoreAsync`
  (ModelMaterializationMatrixTests.cs is the synthetic matrix to extend).

---

### Task 1: Refusal inventory probe at `d2ee536c` (diagnostic; maintainer checkpoint)

**Files:**
- Create: `.scratchpad/c2-probe/probe.md` (results; scratch, never committed)
- No product or test files change.

**Interfaces:**
- Consumes: `BindingTestHost` from `tests/OpenCode.Sdk.Tools.Tests` (run via a temporary TUnit
  test executed locally, then reverted).
- Produces: the approved **selection scope** for Task 7 — the list of pending operations whose
  only remaining walls are the three envelope walls, partitioned by family, with persistentPty
  explicitly excluded (it rides its own queued batch).

- [ ] **Step 1: Write a temporary probe test** (local-only; reverted in Step 4) in
  `tests/OpenCode.Sdk.Tools.Tests/Generator/Binding/OperationPlanBinderTests.cs`:

```csharp
[Test]
public async Task Probe_Pending_Envelope_Refusals()
{
    var (document, selection, curation) = await BindingTestHost.LoadPinnedInputsAsync();
    foreach (var operationId in document.Operations
                 .Select(static operation => operation.OperationId)
                 .Where(id => !selection.OperationIds.Contains(id)))
    {
        try
        {
            _ = new BindingTestHost().Bind(document, Selection(operationId), curation);
            Console.WriteLine($"BINDS {operationId}");
        }
        catch (BindingException exception)
        {
            foreach (var error in exception.Errors)
            {
                Console.WriteLine($"REFUSED {operationId}: [{error.Category}] {error.Problem}");
            }
        }
    }

    await Assert.That(true).IsTrue();
}
```

  Adapt member names to the real `LoadPinnedInputsAsync`/selection surface (the executor reads
  `BindingTestHost` first; per-operation bind may need the operation's group row synthesized the
  way `AssertOperationRefusalAsync` does — copy that pattern).

- [ ] **Step 2: Run it and capture the inventory**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests --configuration Release -- --treenode-filter "/*/*/*/Probe_Pending_Envelope_Refusals"`
Expected: console lines for every pending operation.

- [ ] **Step 3: Write `.scratchpad/c2-probe/probe.md`**: a table `operationId | family | refusal
  class(es) | falls to C2 alone?`. Partition: (a) envelope-wall-only → selection candidates,
  (b) envelope wall + another wall → mechanism evidence only, (c) persistentPty family → excluded
  (own batch).

- [ ] **Step 4: Revert the probe test** (`git checkout -- tests/…/OperationPlanBinderTests.cs`).

- [ ] **Step 5: CHECKPOINT — maintainer approves the Task 7 selection scope** (families and
  operations). No later task selects anything not on the approved list.

### Task 2: `EnvelopePlan` carries a `TypeReferencePlan` (behavior-preserving refactor)

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/Models/EnvelopePlan.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/EnvelopeFacetBinder.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/EnvelopeEmitter.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/EnvelopeDtoEmitter.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/ResponseAdapterEmitter.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/OperationMethodEmitter.cs` and
  `tools/OpenCode.Sdk.Tools/Generator/Binding/PaginationFacetBinder.cs` (whatever the compiler
  names — chase every `PayloadTypeName` consumer)
- Test: existing suites; no new tests (the refactor is proven by byte-identical output)

**Interfaces:**
- Consumes: `TypeReferencePlan` hierarchy, `TypeSyntaxEmitter.Emit(TypeReferencePlan)`.
- Produces: `EnvelopePlan.PayloadType` (`TypeReferencePlan?`) — for CursorList and
  DataLocationList this is the **full** `ListTypeReferencePlan` whose `ElementType` is the item;
  a new `SerializerTypeNamePolicy.ContextPropertyName(TypeReferencePlan)` returning the
  `OpenCodeJsonContext` accessor name (Task 3 extends it; in this task it must return exactly the
  old string for every currently-bound shape).

- [ ] **Step 1: Replace the string with the plan** in `EnvelopePlan.cs`:

```csharp
/// <summary>
/// Gets the payload type plan — for cursor lists and location lists the full list plan
/// whose element is the item — or <see langword="null"/> for a no-content success.
/// </summary>
public required TypeReferencePlan? PayloadType { get; init; }
```

  (Delete `PayloadTypeName`; keep `LocationTypeName` — the location sibling stays nominal.)

- [ ] **Step 2: Produce plans in `EnvelopeFacetBinder`** — wrap today's named results without
  changing any wall or message:

```csharp
private static NamedTypeReferencePlan Named(string name) => new()
{
    Name = name,
    IsNullable = false,
    JsonNullRepresentation = JsonNullRepresentation.ClrNull,
};

private static ListTypeReferencePlan ListOf(TypeReferencePlan element) => new()
{
    ElementType = element,
    IsNullable = false,
    JsonNullRepresentation = JsonNullRepresentation.ClrNull,
};
```

  `BindBarePayload`/`BindDataPayload` return `Named(name)`; `BindCursorListPayload` and the
  DataLocationList arm return `ListOf(Named(itemName))`; DataLocation (object) returns
  `Named(name)`.

- [ ] **Step 3: Emit from the plan.** `EnvelopeEmitter.PayloadType(envelope)` becomes
  `TypeSyntaxEmitter.Emit(envelope.PayloadType!)` (the kind-based `IReadOnlyList` wrapping
  deletes — the plan already carries it). `EnvelopeDtoEmitter.EmitDataType` likewise. In
  `ResponseAdapterEmitter.EmitSuccessCreation`, `readTypeName` becomes
  `envelope.EnvelopeDtoTypeName ?? SerializerTypeNamePolicy.ContextPropertyName(envelope.PayloadType!)`.
  `PaginationFacetBinder`/`OperationMethodEmitter` item-type reads pattern-match
  `ListTypeReferencePlan.ElementType`.

- [ ] **Step 4: Add `SerializerTypeNamePolicy`**
  (`tools/OpenCode.Sdk.Tools/Generator/Emission/SerializerTypeNamePolicy.cs`):

```csharp
internal static class SerializerTypeNamePolicy
{
    /// <summary>Names the OpenCodeJsonContext accessor for a payload read.</summary>
    public static string ContextPropertyName(TypeReferencePlan plan) => plan switch
    {
        NamedTypeReferencePlan named => named.Name,
        _ => throw new InvalidOperationException(
            $"No context accessor exists for plan '{plan.GetType().Name}'; register it in Task 3."),
    };
}
```

- [ ] **Step 5: Prove behavior preservation**

Run: `dotnet run --file tools/opencode-tool.cs -- generate --verify`
Expected: "Generated output is current." (byte-identical). Then the full gate.

- [ ] **Step 6: Commit** — `refactor(tools): carry the envelope payload as a TypeReferencePlan`

### Task 3: Typed serializer-registry entries for bare container payloads

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/Models/RegistryPlan.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/SpecBinder.cs` (`ComposeRegistry`)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/RegistryEmitter.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/SerializerTypeNamePolicy.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests/Generator/Emission/RegistryEmitterTests.cs` (+ snapshot)

**Interfaces:**
- Consumes: `EnvelopePlan.PayloadType`, `TypeSyntaxEmitter.Emit`.
- Produces: `RegistryPlan.PayloadEntries` (`IReadOnlyList<TypeReferencePlan>`) — the bare
  non-named payload plans; `RegistryEmitter` emits for each
  `[JsonSerializable(typeof(<Emit(plan)>), TypeInfoPropertyName = "<ContextPropertyName(plan)>")]`;
  `ContextPropertyName` extends: `ListTypeReferencePlan` → `$"{Inner}List"`,
  `DictionaryTypeReferencePlan` → `$"{Inner}Dictionary"` (recursing on the element/value name),
  refusing nullable/special-number roots until a payload needs them.

- [ ] **Step 1: Write the failing snapshot test** — a registry plan with one list entry renders
  the pinned attribute:

```csharp
[Test]
public async Task Emit_Should_Register_A_Bare_List_Payload_With_A_Pinned_Accessor_Name()
{
    var plan = new RegistryPlan
    {
        TypeNames = ["WidgetInfo"],
        PayloadEntries =
        [
            new ListTypeReferencePlan
            {
                ElementType = new NamedTypeReferencePlan
                {
                    Name = "WidgetInfo",
                    IsNullable = false,
                    JsonNullRepresentation = JsonNullRepresentation.ClrNull,
                },
                IsNullable = false,
                JsonNullRepresentation = JsonNullRepresentation.ClrNull,
            },
        ],
    };
    var source = RegistryEmitter.Emit(plan).Single();
    await Assert.That(source.Content).Contains(
        "[JsonSerializable(typeof(IReadOnlyList<WidgetInfo>), TypeInfoPropertyName = \"WidgetInfoList\")]");
}
```

- [ ] **Step 2: Run it to fail** (`PayloadEntries` does not exist).
- [ ] **Step 3: Implement** `PayloadEntries` (default empty, ordered emission by accessor name,
  deduplicated by accessor name), the emitter attribute (reuse `TypeSyntaxEmitter.Emit`), and the
  `ContextPropertyName` arms. `ComposeRegistry` collects
  `operation.Envelope?.PayloadType` values that are not `NamedTypeReferencePlan` **and** have no
  DTO (`EnvelopeDtoTypeName is null`).
- [ ] **Step 4: Run the test suite + `generate --verify`** (registry snapshot test may need its
  verified file regenerated only if content changed — it must NOT change yet: no bare container
  payload binds until Task 4).
- [ ] **Step 5: Commit** — `feat(tools): register bare container payloads in the serializer context`

### Task 4: Container payloads bind (list + dictionary) through `TypePlanBinder`

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/EnvelopeFacetBinder.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/OperationFacetContext.cs` (carry a
  `TypePlanBinder`, constructed in `SpecBinder` beside the existing one)
- Test: `tests/OpenCode.Sdk.Tools.Tests/Generator/Binding/OperationPlanBinderTests.cs`,
  `tests/OpenCode.Sdk.Tools.Tests/Generator/Emission/ModelMaterializationMatrixTests.cs`

**Interfaces:**
- Consumes: `TypePlanBinder.Bind(schemaKey, propertyName, schema)`; Task 3's registry channel.
- Produces: `BindDataPayload`/`BindBarePayload`/`BindDataLocationPayload` delegate any non-named
  node to the type machinery; the refusal message for an unbindable payload is
  `"success payload does not bind to a supported type plan"` (operation category, after the
  schema-category detail the type binder already recorded).

- [ ] **Step 1: Write failing binding tests** (synthetic; one per shape × kind):

```csharp
[Test]
public async Task Bind_Should_Accept_A_Data_Envelope_Wrapping_A_List_Of_Named_Models()
{
    var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
        .WithSchema("WidgetInfo", schema => schema
            .Type("object")
            .Property("id", property => property.Type("string"), required: true))
        .WithSchema("WidgetListEnvelope", schema => schema
            .Type("object")
            .Property("data", property => property.Array(items => items.Ref("WidgetInfo")), required: true))
        .WithOperation("v2.widget.list", path: "/api/widget", configure: operation => operation
            .Response(200, "application/json", schema => schema.Ref("WidgetListEnvelope")))));

    var plan = BindWidgets(document, "v2.widget.list");

    var operation = plan.Clients.Single().Operations.Single();
    await Assert.That(operation.Envelope!.Kind).IsEqualTo(EnvelopeKind.Data);
    await Assert.That(operation.Envelope!.PayloadType).IsTypeOf<ListTypeReferencePlan>();
}
```

  Sibling tests: Data wrapping a dictionary (`.AdditionalProperties(values => values.Ref(...))`
  — mirror however the scenario builder spells free-form/dictionary values; the executor reads
  `SpecScenario` first), Bare list, Bare dictionary, DataLocation `data` dictionary, and the
  negative: a Data payload that is a tuple/unsupported node still refuses (fragment
  `"does not bind to a supported type plan"`). Adjust the fluent spelling to the real builder.

- [ ] **Step 2: Run to fail** (today's messages: `"must be a required reference to a named
  schema"` / `"must reference a named schema"`).
- [ ] **Step 3: Implement the delegation.** In each payload binder: keep the wrapper-shape walls
  (required `data`, property counts) exactly as they are; where the current code demands
  `RefNode`→named, first keep the named fast path, then fall through to
  `_context.Types.Bind(...)` (the facet-context `TypePlanBinder`), refusing with the new message
  when it returns null. Cursor-list is untouched.
- [ ] **Step 4: Extend the materialization matrix** — one synthetic operation per new payload
  shape flows binder → emitters → `GeneratedSourceCompiler.CompileAndLoadWithSdkCoreAsync` →
  round-trips a fixture body (`{"data":[{"id":"a"}]}`, `{"data":{"k":{"id":"a"}}}`, bare `[…]`,
  bare `{…}` object-map). Assert the adapter materializes the typed payload and the DTO wall
  still fails a missing `data`.
- [ ] **Step 5: Full gate + `generate --verify`** (profile unchanged → generated output unchanged).
- [ ] **Step 6: Commit** — `feat(tools): bind container envelope payloads through the type machinery`

### Task 5: Promoted inline object payloads (deterministic operation-scoped naming)

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/EnvelopeFacetBinder.cs` (naming hook)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/SchemaNameResolver.cs` (op-scoped payload
  name derivation) and/or `tools/OpenCode.Sdk.Tools/Generator/Binding/SchemaPlanBinder.cs`
  (model-closure admission for the promoted payload key)
- Modify: `tools/curation.json` only if a collision forces a reasoned naming row
- Test: `OperationPlanBinderTests.cs`, `ModelMaterializationMatrixTests.cs`

**Interfaces:**
- Consumes: ingestion's op-scoped graph keys (`op:<operationId>#/…`), the existing naming walls
  (spine collision, writer shadow wall), Task 4's delegation.
- Produces: an inline `ObjectNode` at payload position binds as a **named model** whose
  deterministic name is `{ResponseTypeName stem}Data` — `ResponseTypeName` minus its `Response`
  suffix plus `Data` (e.g. `WidgetStatsGetResponse` → `WidgetStatsGetData`) — overridable by a
  reasoned naming row; the promoted model joins the model closure, registry, and manifest like
  any component model.

- [ ] **Step 1 (verify before code):** confirm with a synthetic ingest that an inline object under
  a response `data` member is promoted into `document.Schemas` under an `op:` key, and record the
  exact key shape in the task log. If ingestion does not promote at that position, the promotion
  is added in `ProjectionState` following the existing stream-cause promotion path — smallest
  change wins; the executor documents which branch was real.
- [ ] **Step 2: Write the failing test** — inline object payload binds and emits a model:

```csharp
[Test]
public async Task Bind_Should_Promote_An_Inline_Object_Envelope_Payload()
{
    var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
        .WithSchema("WidgetStatsEnvelope", schema => schema
            .Type("object")
            .Property("data", property => property
                .Type("object")
                .Property("count", inner => inner.Type("integer"), required: true), required: true))
        .WithOperation("v2.widget.stats", path: "/api/widget/stats", configure: operation => operation
            .Response(200, "application/json", schema => schema.Ref("WidgetStatsEnvelope")))));

    var plan = BindWidgets(document, "v2.widget.stats");

    var envelope = plan.Clients.Single().Operations.Single().Envelope!;
    await Assert.That(envelope.PayloadType).IsTypeOf<NamedTypeReferencePlan>();
    await Assert.That(((NamedTypeReferencePlan)envelope.PayloadType!).Name).IsEqualTo("WidgetStatsGetData");
    await Assert.That(plan.Models.Any(static model => model.TypeName == "WidgetStatsGetData")).IsTrue();
}
```

  (Adjust the model-roster assertion to the real `EmitPlan` member; add the collision negative:
  a payload whose derived name collides with an existing component refuses at the naming wall.)
- [ ] **Step 3: Implement.** In the payload delegation path, when the resolved node is an inline
  nominal (`ObjectNode`/`EnumNode`/`UnionNode` behind the op-scoped key), assign the deterministic
  name into the type-name map for that graph key before `TypePlanBinder` runs (so
  `BindReference` finds it), and admit the key into the model closure the same way reachable
  component schemas are admitted. Bare wrappers (like `persistentPty.handoff`'s
  `{"handoff": …}`) take the same path: the wrapper itself is the payload model.
- [ ] **Step 4: Matrix leg** — the promoted model source-generates, compiles, and round-trips
  `{"data":{"count":3}}`.
- [ ] **Step 5: Full gate + `generate --verify`.**
- [ ] **Step 6: Commit** — `feat(tools): promote inline envelope payloads with operation-scoped names`

### Task 6: Represented nullable payloads (response-state guard)

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/EnvelopeFacetBinder.cs` (accept
  `NullableNode` through the same delegation; no special casing)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/EnvelopeEmitter.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/EnvelopeDtoEmitter.cs`
- Test: `OperationPlanBinderTests.cs`, `ModelMaterializationMatrixTests.cs`, and the existing
  non-nullable `{"data":null}` refusal test (must stay green, untouched)

**Interfaces:**
- Consumes: `TypeReferencePlan.IsNullable` (Task 2's plan channel).
- Produces: for `PayloadType.IsNullable`, the DTO `Data` property emits `required T?`; the public
  response record emits the payload property as `T?` whose getter guards on **response state**:

```csharp
// generated shape (illustrative)
public required WidgetInfo? Widget
{
    get => !IsError ? _widget
        : throw new InvalidOperationException("The response is an error; check IsError before accessing Widget.");
    init => _widget = value;
}
```

  The error constructor keeps assigning null; the success path materializes wire null as CLR
  null with `IsError == false`. `PrintMembers` prints `Widget = ` with the null payload rendered
  as empty (match the record's default null rendering).

- [ ] **Step 1: Write the failing binding test** — a Data envelope whose `data` is
  `anyOf [Ref, null]` binds with `PayloadType.IsNullable == true`; the non-nullable refusal case
  stays pinned by the existing test.
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement the emitter changes** — guard selection keys off
  `envelope.PayloadType!.IsNullable`: nullable payloads take the `IsError`-guard shape above;
  non-nullable payloads keep today's `_field ?? throw` shape byte-for-byte (behavior-preserving
  for the whole existing surface, proven again by `generate --verify`).
- [ ] **Step 4: Matrix legs** — success `{"data":null}` on a nullable payload materializes CLR
  null with `IsError == false` and the getter returns null without throwing; the error path
  throws the guard; a nullable payload with a present object round-trips; and the **non-nullable**
  `{"data":null}` case still fails materialization (`RespectNullableAnnotations` wall).
- [ ] **Step 5: Full gate + `generate --verify`.**
- [ ] **Step 6: Commit** — `feat(tools): represent nullable envelope payloads by response state`

M**Addendum (maintainer-approved 2026-08-28): DataLocationList inline-item promotion.** The
mechanism-symmetry extension accepted for `vcs.branches`: `BindDataLocationPayload`'s array arm
admits an inline-object item by promoting it with the same operation-scoped name Task 5 built
(`{ResponseTypeName stem}Data` — the item model). Task 4's guard originally existed to stop
pointer-derived names leaking; Task 5 dissolved that reason, so the guard's array arm narrows:
a `RefNode` item that fails the nominal lookup still refuses (no resurrection), an inline
`ObjectNode` item flows through promotion + `TypePlanBinder`. ADR-0017's cursor-list nominal rule
is untouched. Steps mirror Task 5's: failing binding test (location envelope with array-of-inline
`data` binds; `PayloadType` is `ListTypeReferencePlan` whose element is the promoted named plan) →
red → implement → matrix leg (compile + round-trip `{"data":[{…}],"location":{…}}`) → collision
negative reuses Task 5's wall → gate. Commit (second commit of this task):
`feat(tools): promote inline location-envelope list items` (no trailers).

### Task 7: Selection batches for the approved inventory (instantiated from Task 1)

**Files (per family batch):**
- Modify: `tools/curation.json` (reason-bearing group rows; naming rows only where Task 1's
  probe showed a collision or an unpronounceable derived name)
- Modify: `src/OpenCode.Sdk/*` (regenerated), `src/OpenCode.Sdk/.generation-incomplete`
- Modify: `tests/OpenCode.Sdk.Tests/Snapshots/PublicApi.verified.txt` (reviewed additive diff)
- Test: contract tests per operation family following the A-series pattern
  (`tests/OpenCode.Sdk.Tests/…` — copy the structure of an existing family's contract tests, e.g.
  the Shells family), Extensions roster test growth
- Modify: `docs/ROADMAP.md` (profile counts) in the same commit

**Interfaces:**
- Consumes: Tasks 2–6 mechanisms; the Task 1 checkpoint's approved list.
- Produces: the operations selected, callable, and contract-tested; profile counts move.

- [ ] **Step 1:** For each approved family (doc 21 §4 names `worktree.*`, `workspace.create`,
  `vcs.branches`, `session.stats` among the refused — the checkpoint list is authoritative): add
  the group curation row with its reason, add the operations to the selection.
- [ ] **Step 2:** `dotnet run --file tools/opencode-tool.cs -- generate`; review the regenerated
  diff operation by operation (wire shapes against `spec/openapi.json`).
- [ ] **Step 3:** Write the family's contract tests red-first against recorded wire fixtures
  (success shape, one declared error, `NoThrow`, and the new payload-shape specifics: list
  payloads materialize items; nullable payloads materialize null).
- [ ] **Step 4:** PublicApi baseline: run tests, review the received diff (additive only), accept.
- [ ] **Step 5:** Full gate; ROADMAP profile counts updated in the same commit.
- [ ] **Step 6: Commit per family** — `feat(sdk): select the <family> family through envelope completion`
- [ ] **Step 7 (once, after the last family):** extend the committed sandbox walkthrough with one
  representative new operation and run it live against a pinned-built server; record the output
  in the research log entry (Task 8).

### Task 8: Canon, research log, and closure

**Files:**
- Modify: `docs/architecture/protocol-and-generation.md` (envelope paragraph under "Operations,
  streams, and exclusions" or "Generated model shape" — maintainer approves wording)
- Modify: `docs/research/00-research-log.md` (the arc's Q entry: decisions, probe inventory,
  live evidence)
- Modify: `docs/ROADMAP.md` (status paragraph; queue advances past envelope completion)

**Interfaces:**
- Consumes: everything above.
- Produces: canon states the rule: *an envelope payload is accepted when its ingested schema
  binds to a supported type plan (named, list, dictionary, promoted inline with operation-scoped
  naming, represented nullable distinguished by response state); unsupported nodes fail closed;
  the cursor-list item stays nominal (ADR-0017); no family-specific shape exceptions.*

- [ ] **Step 1:** Draft the canon paragraph and the research-log entry; present both to the
  maintainer with the final whole-arc diff.
- [ ] **Step 2:** Full gate one last time (slopwatch, build, formats, tests, tool smoke,
  `generate --verify`).
- [ ] **Step 3: Commit** — `docs: record envelope completion in canon and the research log`
- [ ] **Step 4:** Request the arc's independent review (repo convention: fresh-context review
  before the arc closes); fix wave if findings; then the plan retires.

## Decisions — resolved at plan review (maintainer, 2026-08-28)

1. **Inline payload naming default**: `{ResponseTypeName stem}Data` (e.g. `SessionStatsGetData`),
   reasoned naming row as the override. Alternative rejected: naming from the wrapper
   component (leaks upstream stabilize names).
2. **Registry accessor scheme** for bare containers: `<Element>List` / `<Value>Dictionary` pinned
   via `TypeInfoPropertyName` (never STJ's default mangling).
3. **Bare nullable payloads**: supported only if Task 1's probe shows a real operation needs them;
   otherwise refused with a named message (`"a bare success body cannot represent null"`) — the
   mechanism stays uniform per shape, not per family.
4. **Task 7 batch granularity**: one commit per family (A-series pattern).
5. **Resolved (maintainer, 2026-08-28):** the live sandbox leg (Task 7 Step 7) is required in
   this arc — the committed sandbox walkthrough is an integral part of feature arcs at least
   until the M4 fixture stands up proper functional-test infrastructure.
